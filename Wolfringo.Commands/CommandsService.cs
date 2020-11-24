﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TehGM.Wolfringo.Commands.Initialization;
using TehGM.Wolfringo.Commands.Results;
using TehGM.Wolfringo.Messages;
using TehGM.Wolfringo.Utilities.Internal;

namespace TehGM.Wolfringo.Commands
{
    /// <summary>A service that deals with commands loading, initialization and execution.</summary>
    /// <remarks><para>This is a default service that runs the commands. It'll manage all other parts of Commands System.</para>
    /// <para>This command service can be customized partially by injecting custom services into its constructor. If these services are set to default or skipped, default instances will be automatically created and used, similarly to <see cref="WolfClient"/>.</para></remarks>
    public class CommandsService : ICommandsService, IDisposable
    {
        private readonly IWolfClient _client;
        private readonly CommandsOptions _options;
        private readonly IServiceProvider _services;
        private readonly ICommandHandlerProvider _handlerProvider;
        private readonly ICommandInitializerMap _initializers;
        private readonly ICommandsLoader _commandsLoader;
        private readonly ILogger _log;
        private CancellationTokenSource _cts;

        private List<IDisposable> _disposables;
        private readonly bool _disposeHandlerProvider;
        private bool _started;
        private readonly SemaphoreSlim _lock;
        private readonly IDictionary<ICommandInstanceDescriptor, ICommandInstance> _commands;

        /// <summary>Initializes a command service.</summary>
        /// <param name="client">WOLF client. Required.</param>
        /// <param name="options">Commands options that will be used as default when running a command. Required.</param>
        /// <param name="services">Services provider that will be used by all commands. Null will cause a default to be used.</param>
        /// <param name="handlerProvider">Handler provider that deals with creation and caching of handler objects. Null will cause a default to be used.</param>
        /// <param name="initializers">Map of command initializers for each command attribute. Null will cause a default to be used.</param>
        /// <param name="commandsLoader">Service that loads command attributes from assemblies and types. Null will cause a default to be used.</param>
        /// <param name="log">Logger to log messages and errors to. If null, all logging will be disabled.</param>
        /// <param name="cancellationToken">Cancellation token that can be used for cancelling all tasks.</param>
        public CommandsService(IWolfClient client, CommandsOptions options, IServiceProvider services = null, ICommandHandlerProvider handlerProvider = null, ICommandInitializerMap initializers = null, ICommandsLoader commandsLoader = null, ILogger log = null, CancellationToken cancellationToken = default)
        {
            // init required
            this._client = client ?? throw new ArgumentNullException(nameof(client));
            this._options = options ?? throw new ArgumentNullException(nameof(options));

            // init optionals
            this._log = log;
            this._services = services ?? this.CreateDefaultServiceProvider();
            this._handlerProvider = handlerProvider;
            if (this._handlerProvider == null)
            {
                this._handlerProvider = new CommandHandlerProvider(this._services);
                this._disposeHandlerProvider = true;
            }
            this._initializers = initializers ?? new DefaultCommandInitializerMap();
            this._commandsLoader = commandsLoader ?? new DefaultCommandsLoader(this._initializers, this._log);

            // init private
            this._commands = new Dictionary<ICommandInstanceDescriptor, ICommandInstance>();
            this._disposables = new List<IDisposable>();
            this._lock = new SemaphoreSlim(1, 1);
            this._cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            // register event handlers
            this._client.AddMessageListener<ChatMessage>(OnMessageReceived);
        }

        private IServiceProvider CreateDefaultServiceProvider()
        {
            IDictionary<Type, object> servicesMap = new Dictionary<Type, object>
                {
                    { typeof(IWolfClient), this._client },
                    { this._client.GetType(), this._client },
                    { typeof(CommandsOptions), this._options }
                };
            if (this._log != null)
            {
                servicesMap.Add(typeof(ILogger), this._log);
                servicesMap.Add(this._log.GetType(), this._log);
            }
            return new SimpleServiceProvider(servicesMap);
        }

        /// <inheritdoc/>
        /// <remarks>Once already started, calling this method again will cause commands to be re-loaded.</remarks>
        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            using (CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, this._cts.Token))
            {
                await this._lock.WaitAsync(cts.Token).ConfigureAwait(false);
                try
                {
                    this._log?.LogDebug("Initializing commands");

                    this._commands.Clear();
                    this._disposables.Clear();

                    // ask loader to load from all specified assemblies and types
                    IEnumerable<ICommandInstanceDescriptor> descriptors = await _commandsLoader.LoadFromAssembliesAsync(_options.Assemblies ?? Enumerable.Empty<Assembly>(), cts.Token).ConfigureAwait(false);
                    descriptors = descriptors.Union(await _commandsLoader.LoadFromTypesAsync(_options.Classes.Select(t => t.GetTypeInfo()) ?? Enumerable.Empty<TypeInfo>(), cts.Token).ConfigureAwait(false));

                    // make sure there's no duplicates
                    descriptors = descriptors.Distinct();

                    // for each loaded command, handle pre-initialization and caching
                    foreach (ICommandInstanceDescriptor descriptor in descriptors)
                    {
                        CommandHandlerAttribute handlerAttribute = descriptor.GetHandlerAttribute();

                        // check if handler is meant to be pre-initialized. If so, request it from provider to pre-initialize
                        if (handlerAttribute?.PreInitialize == true)
                        {
                            this._log?.LogDebug("Pre-initializing command handler {Handler}", descriptor.GetHandlerType().Name);
                            this._handlerProvider.GetCommandHandler(descriptor);
                        }

                        // create all command instances
                        this._log?.LogDebug("Creating command instance {Name} from handler {Handler}", descriptor.Method.Name, descriptor.GetHandlerType().Name);
                        ICommandInitializer initializer = this._initializers.GetMappedInitializer(descriptor.Attribute.GetType());
                        ICommandInstance instance = initializer.InitializeCommand(descriptor, _options);
                        this._commands.Add(descriptor, instance);

                        // grab disposables so we can easily dispose them when we dispose this object
                        if (descriptor is IDisposable disposableDescriptor)
                            this._disposables.Add(disposableDescriptor);
                        if (instance is IDisposable disposableInstance)
                            this._disposables.Add(disposableInstance);
                    }

                    // re-add handler provider to disposables if needed
                    if (_disposeHandlerProvider)
                        this._disposables.Add(this._handlerProvider as IDisposable);

                    // mark as started
                    this._started = true;
                    this._log?.LogDebug("{Count} commands loaded", _commands.Count);
                }
                finally
                {
                    this._lock.Release();
                }
            }
        }

        // 2 ways to handle errors:
        // if executed from OnMessageReceived event handler, we need to capture all errors and log them, otherwise they're lost - ExecuteAsyncInternal will log per-command scope, so we just need to catch whatever was outside
        // for the public ExecuteAsync, assume caller will handle exceptions on their own - for this reason, capture exception from ICommandResult, and rethrow it. Use ExceptionDispatchInfo to not lose stack trace

        private async void OnMessageReceived(ChatMessage message)
        {
            if (!this._started)
                return;
            try
            {
                ICommandContext context = new CommandContext(message, this._client, this._options);
                await ExecuteAsyncInternal(context).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex.LogAsError(this._log, "Unhandled exception when executing commands")) { }
        }

        /// <inheritdoc/>
        public async Task<ICommandResult> ExecuteAsync(ICommandContext context, CancellationToken cancellationToken = default)
        {
            ICommandResult result = await ExecuteAsyncInternal(context, cancellationToken).ConfigureAwait(false);
            if (result.Exception != null)
                ExceptionDispatchInfo.Capture(result.Exception).Throw();
            return result;
        }

        private async Task<ICommandResult> ExecuteAsyncInternal(ICommandContext context, CancellationToken cancellationToken = default)
        {
            if (!this._started)
                throw new InvalidOperationException($"This {this.GetType().Name} is not started yet");
            using (CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, this._cts.Token))
            {
                IEnumerable<KeyValuePair<ICommandInstanceDescriptor, ICommandInstance>> commandsCopy;
                // copying might not be the fastest thing to do, but it'll ensure that commands won't be changed out of the lock
                // locking only copying to prevent hangs if user is not careful with their commands, while still preventing race conditions with StartAsync
                await this._lock.WaitAsync(cts.Token).ConfigureAwait(false);
                try
                {
                    // order commands by priority
                    // try to get from concrete Descriptor if possible, as it should be precached and avoid additional reflection and thus faster
                    commandsCopy = this._commands.OrderByDescending(kvp => (kvp.Key is CommandInstanceDescriptor cid) ? cid.Priority : kvp.Key.GetPriority());
                }
                finally
                {
                    this._lock.Release();
                }

                foreach (KeyValuePair<ICommandInstanceDescriptor, ICommandInstance> commandKvp in commandsCopy)
                {
                    ICommandInstanceDescriptor command = commandKvp.Key;
                    ICommandInstance instance = commandKvp.Value;
                    using (this._log.BeginCommandScope(context, command.GetHandlerType().Name, command.Method.Name))
                    {
                        ICommandHandlerProviderResult handlerResult = null;
                        try
                        {
                            cts.Token.ThrowIfCancellationRequested();

                            // check if the command should run at all - if not, skip
                            ICommandResult matchResult = await instance.CheckMatchAsync(context, this._services, cts.Token).ConfigureAwait(false);
                            if (!matchResult.IsSuccess)
                                continue;

                            // initialize handler
                            _log?.LogTrace("Initializing handler handler {Handler} for command {Name}", command.GetHandlerType().Name, command.Method.Name);
                            handlerResult = _handlerProvider.GetCommandHandler(command);
                            if (handlerResult?.HandlerInstance == null)
                            {
                                _log?.LogError("Retrieving handler {Handler} for command {Name} has failed, command execution aborting", command.GetHandlerType().Name, command.Method.Name);
                                return CommandExecutionResult.FromException(new ArgumentNullException(nameof(ICommandHandlerProviderResult.HandlerInstance),
                                    $"Retrieving handler {command.GetHandlerType().Name} for command {command.Method.Name} has failed, command execution aborting"));
                            }
                            _log?.LogTrace("Executing command {Name} from handler {Handler}", command.Method.Name, command.GetHandlerType().Name);

                            // execute the command
                            ICommandResult executeResult = await instance.ExecuteAsync(context, _services, matchResult, handlerResult.HandlerInstance, cts.Token).ConfigureAwait(false);
                            if (executeResult.Exception != null)
                            {
                                this._log?.LogError(executeResult.Exception, "Exception when executing command {Name} from handler {Handler}", command.Method.Name, command.GetHandlerType().Name);
                                return executeResult;
                            }
                            if (executeResult is IMessagesCommandResult messagesResult && messagesResult.Messages?.Any() == true)
                            {
                                this._log?.LogTrace("Sending command results messages as a command response");
                                bool replyGroup = context.Message.IsGroupMessage;
                                uint replyRecipientID = replyGroup ? context.Message.RecipientID : context.Message.SenderID.Value;
                                string text = string.Join("\n", messagesResult.Messages);
                                await context.Client.SendAsync(new ChatMessage(replyRecipientID, replyGroup, ChatMessageTypes.Text, Encoding.UTF8.GetBytes(text)), cts.Token).ConfigureAwait(false);
                            }
                            return executeResult;
                        }
                        catch (OperationCanceledException ex)
                        {
                            _log?.LogWarning("Execution of command {Name} from handler {Handler} was cancelled", command.Method.Name, command.GetHandlerType().Name);
                            return CommandExecutionResult.FromException(ex);
                        }
                        catch (Exception ex) when (ex.LogAsError(_log, "Unhandled Exception when executing command {Name} from handler {Handler}", command.Method.Name, command.GetHandlerType().Name))
                        {
                            return CommandExecutionResult.FromException(ex);
                        }
                        finally
                        {
                            // if handler is allocated, not persistent and disposable, let's dispose it
                            if (handlerResult?.Descriptor?.Attribute?.IsPersistent != true && handlerResult?.HandlerInstance is IDisposable disposableHandler)
                                try { disposableHandler?.Dispose(); } catch { }
                        }
                    }
                }
                return CommandExecutionResult.Failure;
            }
        }

        /// <summary>Disposes the Command Service.</summary>
        /// <remarks><para>This method will dispose services created by default if they weren't provided via the constructor. If service was provided via constructor, it will NOT be disposed - please dispose it manually when convenient.</para>
        /// <para>This method will also dispose all command instances and descriptors that happen to implement <see cref="IDisposable"/>.</para></remarks>
        public void Dispose()
        {
            // remove event listener
            this._client.RemoveMessageListener<ChatMessage>(OnMessageReceived);
            // cancel all tasks
            try { this._cts?.Cancel(); } catch { }
            try { this._cts?.Dispose(); } catch { }
            // purge collections
            this._commands?.Clear();
            // dispose all objects (such as instances, descriptors, or services) that implement IDisposable
            foreach (IDisposable disposable in this._disposables)
                try { disposable?.Dispose(); } catch { }
            this._disposables.Clear();
            // dispose semaphore
            try { _lock?.Dispose(); } catch { }
        }
    }
}
