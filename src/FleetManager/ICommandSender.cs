using Contracts;

namespace FleetManager;

public interface ICommandSender
{
    Task<CommandSendResult> SendAsync(MoveCommandMessage command, CancellationToken cancellationToken);
}