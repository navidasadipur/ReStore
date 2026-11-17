namespace ReStore.Application.Interfaces
{
    /// <summary>
    /// Who the basket belongs to.
    ///
    /// The controller used to work this out inline — <c>User.Identity?.Name</c> or the
    /// <c>buyerId</c> cookie — and every basket handler would have had to repeat it. It
    /// lives behind this interface so Application can ask the question without knowing
    /// anything about HTTP, cookies or claims, and so a test can answer it directly.
    /// </summary>
    public interface IBuyerIdentity
    {
        /// <summary>
        /// The signed-in user's name, or the anonymous <c>buyerId</c> cookie, or null when
        /// the visitor has neither.
        /// </summary>
        string? BuyerId { get; }

        /// <summary>The guest cookie independently of the current authenticated identity.</summary>
        string? AnonymousBuyerId { get; }

        /// <summary>True when <see cref="BuyerId"/> came from an authenticated user.</summary>
        bool IsAuthenticated { get; }

        /// <summary>
        /// Creates an anonymous buyer id **and** writes the cookie, so it survives the
        /// request that created it. Only called for visitors who are not signed in.
        /// </summary>
        string CreateAnonymousBuyerId();

        /// <summary>Removes the anonymous cookie (used when an anonymous basket is merged into a user's).</summary>
        void ClearAnonymousBuyerId();
    }
}
