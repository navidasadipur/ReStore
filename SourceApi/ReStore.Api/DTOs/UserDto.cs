namespace ReStore.Api.DTOs
{
    public class UserDto
    {
        public string Email { get; set; }
        public string Token { get; set; }
        public BasketDto BasketEntity {get; set; }
    }
}