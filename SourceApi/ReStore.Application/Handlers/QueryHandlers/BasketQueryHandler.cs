using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using ReStore.Application.DTOs;
using ReStore.Application.Interfaces;
using ReStore.Application.Queries;

namespace ReStore.Application.Handlers.QueryHandlers
{
    /// <summary>
    /// Reads the visitor's basket.
    ///
    /// "No basket yet" is a **successful** result with no data, not a failure: an empty
    /// basket is the normal state of a first-time visitor, and the client treats a missing
    /// basket as nothing to show rather than as an error message.
    /// </summary>
    public class BasketQueryHandler : IRequestHandler<BasketQuery, DtoReturnOne<BasketDto>>
    {
        private readonly IBasketRepository _repository;
        private readonly IBuyerIdentity _buyerIdentity;
        private readonly IAppLogger _logger;

        public BasketQueryHandler(
            IBasketRepository repository,
            IBuyerIdentity buyerIdentity,
            IAppLogger logger)
        {
            _repository = repository;
            _buyerIdentity = buyerIdentity;
            _logger = logger;
        }

        public async Task<DtoReturnOne<BasketDto>> Handle(
            BasketQuery request,
            CancellationToken cancellationToken)
            => await BasketResults.ReadAsync(_repository, _buyerIdentity, _logger, cancellationToken);
    }

    /// <summary>
    /// Shared by the query and the two basket commands: they all answer with "the basket
    /// as it now stands", so the response cannot drift between them.
    /// </summary>
    internal static class BasketResults
    {
        public static async Task<DtoReturnOne<BasketDto>> ReadAsync(
            IBasketRepository repository,
            IBuyerIdentity buyerIdentity,
            IAppLogger logger,
            CancellationToken cancellationToken)
        {
            var buyerId = buyerIdentity.BuyerId;
            if (string.IsNullOrWhiteSpace(buyerId))
                return Empty();

            var response = await repository.GetWithItemsAsync(buyerId, cancellationToken);

            if (response.Result is false)
            {
                // A basket that does not exist yet is not an error.
                if (response.Data is null)
                    return Empty();

                return Failure(response.Error);
            }

            return response.Data is null ? Empty() : Success(response.Data);
        }

        public static DtoReturnOne<BasketDto> Success(Domain.Entities.BasketEntity basket)
        {
            var result = new DtoReturnOne<BasketDto>();
            result.SetResultFrom(new List<BasketDto> { BasketDto.FromEntity(basket) }, 1, 1);
            return result;
        }

        public static DtoReturnOne<BasketDto> Empty()
        {
            var result = new DtoReturnOne<BasketDto>();
            result.SetResultFrom(new List<BasketDto>(), 0, 0);
            return result;
        }

        public static DtoReturnOne<BasketDto> Failure(string? error)
        {
            var result = new DtoReturnOne<BasketDto>();
            result.SetErrorResult(error ?? "The basket could not be read.");
            return result;
        }
    }
}
