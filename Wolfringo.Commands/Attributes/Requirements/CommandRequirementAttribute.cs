using System;
using System.Threading;
using System.Threading.Tasks;

namespace TehGM.Wolfringo.Commands.Attributes
{
    /// <summary>Represents any special validation that message needs to pass for command to be executed.</summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
    public abstract class CommandRequirementAttribute : Attribute, ICommandRequirement
    {
        /// <summary>The message that bot should reply with if requirement was not fulfilled.</summary>
        public string ErrorMessage { get; set; }

        /// <summary>Checks requirement.</summary>
        /// <param name="context">Command to check the requirement for.</param>
        /// <param name="services">Services that can be used during requirement checks.</param>
        /// <param name="cancellationToken">Token for cancelling the task.</param>
        /// <returns>True if requirement was fullfilled; otherwise false.</returns>
        public abstract Task<bool> CheckAsync(ICommandContext context, IServiceProvider services, CancellationToken cancellationToken = default);
    }
}
