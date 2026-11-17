namespace ReStore.Domain.Common
{
    /// <summary>
    /// Opt-in marker for entities that can be deactivated instead of deleted.
    /// <see cref="ReStore.Application.Handlers.BaseHandlers.BaseInActiveHandler{Entity,Command}"/>
    /// requires this, so the handler can exist today while no entity implements
    /// it yet — soft delete is a product decision, not part of the move.
    /// </summary>
    public interface IHasInactive
    {
        bool Inactive { get; set; }
    }
}
