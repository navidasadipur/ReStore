using Microsoft.AspNetCore.Identity;

namespace ReStore.Domain.Entities
{
    public class User: IdentityUser<int>
    {
        public UserAddress Address { get; set; }
    }
}