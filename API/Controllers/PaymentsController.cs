using API.Data;
using API.DTOs;
using API.Entities.OrderAggregare;
using API.Extensions;
using API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Stripe;

namespace API.Controllers
{
    public class PaymentsController : BaseApiController
    {
        private readonly PaymentService _paymentService;
        private readonly StoreContext _context;
        private readonly IConfiguration _config;
        public PaymentsController(PaymentService paymentService, StoreContext context, IConfiguration config)
        {
            _config = config;
            _context = context;
            _paymentService = paymentService;
        }

        [Authorize]
        [HttpPost]
        public async Task<ActionResult<BasketDto>> CreateOrUpdatePaymentIntent()
        {
            var basket = await _context.Baskets
                .RetrieveBasketWithItems(User.Identity.Name)
                .FirstOrDefaultAsync();

            if (basket == null) return NotFound();

            var intent = await _paymentService.CreateOrUpdatePaymentIntent(basket);

            if (intent == null) return BadRequest(new ProblemDetails { Title = "Problem creating payment intent" });

            basket.PaymentIntentId = string.IsNullOrWhiteSpace(basket.PaymentIntentId) ? intent.Id : basket.PaymentIntentId ;
            basket.ClientSecret = string.IsNullOrWhiteSpace(basket.ClientSecret) ? intent.ClientSecret : basket.ClientSecret;

            _context.Update(basket);

            var result = await _context.SaveChangesAsync() > 0;

            if (!result) return BadRequest(new ProblemDetails{Title = "Problem updating basket with intent"});

            return basket.MapBasketToDto();
        }

        [HttpPost("webhook")]
        public async Task<ActionResult> StripeWebhook()
        {
            var json = await new StreamReader(HttpContext.Request.Body).ReadToEndAsync();

            var stripeEvent = EventUtility.ConstructEvent(json, Request.Headers["Stripe-Signature"],
                _config["StripeSettings:WhSecret"]);

            // Stripe sends every event type subscribed to on this endpoint, so a
            // hard cast would throw on anything that is not a charge.
            if (stripeEvent.Data.Object is not Charge charge) return new EmptyResult();

            var order = await _context.Orders.FirstOrDefaultAsync(x =>
                x.PaymentIntentId == charge.PaymentIntentId);

            // No matching order: acknowledge anyway. Returning an error here
            // makes Stripe retry the delivery indefinitely.
            if (order == null) return new EmptyResult();

            // Only act on terminal states — a charge can also be "pending",
            // which says nothing about the outcome yet.
            if (charge.Status == "succeeded") order.OrderStatus = OrderStatus.PaymentReceived;
            else if (charge.Status == "failed") order.OrderStatus = OrderStatus.PaymentFailed;
            else return new EmptyResult();

            await _context.SaveChangesAsync();

            return new EmptyResult();
        }

    }
}