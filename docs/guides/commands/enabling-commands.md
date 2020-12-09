---
uid: Guides.Commands.Intro
---

# Wolfringo Commands System
Using [AddMessageListener](xref:TehGM.Wolfringo.IWolfClient#TehGM_Wolfringo_IWolfClient_AddMessageListener_TehGM_Wolfringo_Utilities_Internal_IMessageCallback_) works well for testing, or for events that aren't [Chat Messages](xref:TehGM.Wolfringo.Messages.ChatMessage). However having a bot written using just the message listeners would mean that you manually need to handle errors logging, prefix checking, checking if user is admin, etc - that's A LOT of repetitive boilerplate code.

To address this, Wolfringo has an extensible Commands System. This System is [attribute-based](xref:Guides.Commands.Attributes), and supports [Dependency Injection](xref:Guides.Commands.DependencyInjection) out of the box.

## Enable Commands System in your bot
Commands System is included in both [Wolfringo metapackage](https://www.nuget.org/packages/Wolfringo) and [Wolfringo.Hosting](https://www.nuget.org/packages/Wolfringo.Hosting). If you're not using either (for example when using [Wolfringo.Core](https://www.nuget.org/packages/Wolfringo.Core) directly), it can be installed with [Wolfringo.Commands](https://www.nuget.org/packages/Wolfringo.Commands) package. See [Installation instructions](xref:Guides.GettingStarted.Installation) for guide how to install Wolfringo components.

Like with the [Bot itself](xref:Guides.GettingStarted.Connecting), enabling Commands System depends on whether you're using Wolfringo.Hosting or not.

### [Without Wolfringo.Hosting (Normal Bot)](#tab/connecting-normal-bot)
First add following using directive to your Program.cs:
```csharp
using Wolfringo.Commands;
```

Commands System's main entry point is @TehGM.Wolfringo.Commands.CommandsSystem class. Its constructor takes minimum of 2 parameters: @TehGM.Wolfringo.IWolfClient and @TehGM.Wolfringo.Commands.CommandsOptions. @TehGM.Wolfringo.IWolfClient should be created by you already - see [Connecting the Bot guide](xref:Guides.GettingStarted.Connecting) if not - so let's skip to creating @TehGM.Wolfringo.Commands.CommandsOptions.

```csharp
CommandsOptions options = new CommandsOptions()
{
    Prefix = "!",
    RequirePrefix = PrefixRequirement.Always,
    CaseSensitivity = false
};
```

Basic Commands Options are pretty straightforward:
- `Prefix` determines what all the commands should start with - for example "!", "!mybot" etc. Default value is "!".
- `RequirePrefix` determines when the prefix is required. By default it's always required, but you can for example make prefix optional by setting this value to [PrefixRequirement.Group](xref:TehGM.Wolfringo.Commands.PrefixRequirement.Group).
- `CaseSensitivity` determines whether commands are case-sensitive. By default, all commands are case-insensitive (the value is set to "false").

Once you have your options set, you can create and start Commands System. You should do it after creating bot client, but before you connect your bot:
```csharp
_client = new WolfClient();
// other client set up here - for example event listeners or reconnecting

CommandsService commands = new CommandsService(_client, options);
await commands.StartAsync();

// connect bot here with "_client.ConnectAsync();"
```

> Note: Calling `commands.StartAsync();` will reload all commands each time. However, it will not dispose persistent handlers. To dispose them, call `commands.Dispose()` and recreate the CommandsSystem entirely.

### Choose where commands are loaded from
By default, all commands in the project that starts your bot process are loaded. You can change that using CommandsOptions.

#### Load commands from other assemblies
You can load commands from other projects, or even different libraries. To do so, simply add assembly to `Assemblies` property:
```csharp
options.Assemblies.Add(typeof(HandlerInAnotherProject).Assembly));
```

#### Add commands individually
You can also add individual [Handler](xref:Guides.Commands.Handlers) to be loaded. Simply add its type to `Classes` property:
```csharp
options.Classes.Add(typeof(Handler));
```

If you're adding handlers individually, you might want to disable behaviour of loading all commands from bot entry assembly.
```csharp
options.Assemblies.Clear();
```

### Logging
Part of purpose of the Commands System is to reduce amount of boilerplate code for logging etc. @TehGM.Wolfringo.Commands.CommandsSystem fully supports logging, however you still need to provide an ILogger instance to its constructor:
```csharp
ILogger log = // ... create logger according to your logging library isntructions
CommandsService commands = new CommandsService(_client, options, log);
```

Check [Logging guide](xref:Guides.Features.Logging) for more information.

### [With Wolfringo.Hosting (.NET Generic Host/ASP.NET Core)](#tab/connecting-hosted-bot)
When using Wolfringo.Hosting, enabling Commands System is done inside ConfigureServices, just like [the bot itself](xref:Guides.GettingStarted.Connecting). Simply add this code:
```csharp
services.AddWolfringoCommands()
    .SetPrefix("!")           
    .SetPrefixRequirement(PrefixRequirement.Always)
    .SetCaseSensitive(false);
```

Basic Commands configuration is pretty straightforward:
- `SetPrefix` determines what all the commands should start with - for example "!", "!mybot" etc. Default value is "!".
- `SetPrefixRequirement` determines when the prefix is required. By default it's always required, but you can for example make prefix optional by setting this value to [PrefixRequirement.Group](xref:TehGM.Wolfringo.Commands.PrefixRequirement.Group).
- `SetCaseSensitive` determines whether commands are case-sensitive. By default, all commands are case-insensitive (the value is set to "false").

> [!TIP]
> Using application settings file is recommended instead of hardcoding the settings. See [appsettings.json example](https://github.com/TehGM/Wolfringo/blob/master/Examples/HostedCommandsBot/appsettings.json), and add following method call to your ConfigureServices:  
> ```csharp
>  services.Configure<CommandsOptions>(context.Configuration.GetSection("Commands"));
>  ```

### Choose where commands are loaded from
By default, all commands in the project that starts your bot process are loaded. You can change that with method calls straight after AddWolfringoCommands().

#### Load commands from other assemblies
You can load commands from other projects, or even different libraries. To do so, simply call `AddHandlers()`:
```csharp
services.AddWolfringoCommands()
    .AddHandlers(typeof(HandlerInAnotherProject).Assembly));
```

#### Add commands individually
You can also add individual [Handler](xref:Guides.Commands.Handlers) to be loaded. Simply call `AddHandler<T>()`:
```csharp
services.AddWolfringoCommands()
    .AddHandler<Handler>();
```

If you're adding handlers individually, you might want to disable behaviour of loading all commands from bot entry assembly.
```csharp
services.AddWolfringoCommands()
    .RemoveDefaultHandlers();
```

### Logging
Part of purpose of the Commands System is to reduce amount of boilerplate code for logging etc. @TehGM.Wolfringo.Hosting.Commands.HostedCommandsHandler will automatically use logging as [configured for your Host](https://docs.microsoft.com/en-gb/aspnet/core/fundamentals/logging/?view=aspnetcore-3.0).

Check [Logging guide](xref:Guides.Features.Logging) for more information.
***

## Next steps
Now that you enabled Commands System in your bot, you can start adding Commands Handlers and commands themselves. See [Creating Commands guide](xref:Guides.Commands.Handlers)!

You can also check [SimpleCommandsBot Example](https://github.com/TehGM/Wolfringo/tree/master/Examples/SimpleCommandsBot) (Normal Bot) or [HostedCommandsBot Example](https://github.com/TehGM/Wolfringo/tree/master/Examples/HostedCommandsBot) (.NET Generic Host/ASP.NET Core) for full example on Wolfringo usage with Commands System. Feel free to also check [other example projects](https://github.com/TehGM/Wolfringo/tree/master/Examples)!