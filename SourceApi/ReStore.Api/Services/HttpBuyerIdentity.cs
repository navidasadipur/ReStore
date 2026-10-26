using System;
using Microsoft.AspNetCore.Http;
using ReStore.Application.Interfaces;

namespace ReStore.Api.Services
{
    /// <summary>
    /// The HTTP answer to <see cref="IBuyerIdentity"/>: an authenticated user is identified
    /// by their name, everyone else by the <c>buyerId</c> cookie.
    ///
    /// This is the only place that knows about the cookie, which is the point: the basket
    /// handlers ask who the buyer is and never touch HttpContext.
    /// </summary>
    public sealed class HttpBuyerIdentity : IBuyerIdentity
    {
        /// <summary>The cookie name the client also reads to decide whether to fetch a basket.</summary>
        public const string CookieName = "buyerId";

        private static readonly CookieOptions CookieOptions = new()
        {
            IsEssential = true,
            Expires = DateTimeOffset.UtcNow.AddDays(30),
            SameSite = SameSiteMode.Lax
        };

        private readonly IHttpContextAccessor _httpContextAccessor;

        public HttpBuyerIdentity(IHttpContextAccessor httpContextAccessor)
            => _httpContextAccessor = httpContextAccessor;

        private HttpContext? Context => _httpContextAccessor.HttpContext;

        public string? BuyerId
        {
            get
            {
                var userName = Context?.User?.Identity?.Name;

                if (!string.IsNullOrWhiteSpace(userName))
                    return userName;

                return Context?.Request.Cookies[CookieName];
            }
        }

        public bool IsAuthenticated => !string.IsNullOrWhiteSpace(Context?.User?.Identity?.Name);

        public string CreateAnonymousBuyerId()
        {
            var buyerId = Guid.NewGuid().ToString();

            Context?.Response.Cookies.Append(CookieName, buyerId, CookieOptions);

            return buyerId;
        }

        public void ClearAnonymousBuyerId()
        {
            var response = Context?.Response;
            if (response is null)
                return;

            if (response.HasStarted)
                return;

            response.Cookies.Delete(CookieName);
        }
    }
}
