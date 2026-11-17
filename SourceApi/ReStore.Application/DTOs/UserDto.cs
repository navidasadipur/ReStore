using ReStore.Domain.Entities;

namespace ReStore.Application.DTOs;

public class UserDto : BaseDto
{
    public string Email { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
    public BasketDto? Basket { get; set; }
}

public class UserAddressDto : BaseDto
{
    public int Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Address1 { get; set; } = string.Empty;
    public string Address2 { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Zip { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;

    public static UserAddressDto FromEntity(UserAddress address) => new()
    {
        Id = address.Id, FullName = address.FullName, Address1 = address.Address1,
        Address2 = address.Address2, City = address.City, State = address.State,
        Zip = address.Zip, Country = address.Country
    };
}
