namespace ReStore.Api.DTOs
{
    public class UserDto
    {
        public string Email { get; set; }
        public string Token { get; set; }

        /// <summary>
        /// Named Basket, not BasketEntity: this is a wire name the React client reads
        /// (user.basket). It was caught having been silently renamed during the entity
        /// rename — a reminder that property names on DTOs are part of the API contract,
        /// so they must never follow an entity rename.
        /// </summary>
        public BasketDto Basket {get; set; }
    }
}
