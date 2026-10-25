using ReStore.Api.DTOs;
using ReStore.Domain.Entities;
using AutoMapper;

namespace ReStore.Api.RequestHelpers
{
    public class MappingProfiles : Profile
    {
        public MappingProfiles()
        {
            CreateMap<CreateProductDto, Product>();
            CreateMap<UpdateProductDto, Product>();
        }
    }
}