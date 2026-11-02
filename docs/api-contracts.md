# API contracts

The base path is `/api/`. These are the current repository contracts, not a claim
about an already-deployed image. [Architecture](architecture.md) explains their
implementation; [README](../README.md) covers configuration and local commands.
Swagger is served at `/swagger/v1/swagger.json`.

## Monetary units during the ZarinPal migration

Product price inputs/outputs and basket prices are integer IRR (rials). The UI displays
tomans (IRR / 10). All orders, including existing demo orders after migration, use rials.
Order totals include the saved delivery fee.
New Stripe payment requests return a failed business envelope while ZarinPal is being
implemented. See [the migration plan](zarinpal-migration-plan.md).

## Envelopes and HTTP statuses

Business handlers return an envelope at HTTP 200 for handled success or failure.
Use `succeeded` to decide the outcome. Do not compare `result` to `true` across all
endpoints: query envelopes use a boolean, while command envelopes use numeric
ReturnResult values (Failed=0, Success=1, Warning=2).

| Envelope | Purpose | Payload |
| --- | --- | --- |
| `PaginatedResponseListDto<T>` | List query | `data` array, `totalCount`, `returnCount` |
| `DtoReturnOne<T>` | Single/nullable result | `data` object or null |
| `InsertReturn` | Created record | `key`, not the full created record |
| `UpdateReturn` / `DeleteReturn` | Write outcome | `succeeded`, optional `error` / `errorCode` |
| `ApiErrorResponse` | HTTP binding/unhandled failure on an opted-in route | Failed command-style envelope; validation adds `errors` |

Examples omit unused/null metadata and use illustrative messages. A partial last
catalog page can look like:

```json
{
  "data": [{ "id": 13, "name": "Shoe" }, { "id": 14, "name": "Hat" }],
  "result": true,
  "succeeded": true,
  "totalCount": 14,
  "returnCount": 2
}
```

For page 3 with pageSize 6, this is still a three-page result. `returnCount` is 2;
the requested page size remains 6. No Pagination response header is required.

An inserted order/user/product returns its key:

```json
{ "result": 1, "succeeded": true, "key": 42 }
```

A binding failure, such as `pageNumber=invalid`, returns HTTP 200:

```json
{
  "result": 0,
  "succeeded": false,
  "error": "The request contains invalid values.",
  "errorCode": "ValidationFailed",
  "errors": { "PageNumber": ["Invalid page number."] }
}
```

EnvelopeResponseAttribute opts feature routes into that policy. Unhandled
exceptions there use `errorCode: "UnhandledError"`; production returns a generic
message. Handler failures may have other error codes or provider wording. Identity
validation errors use messages plus comma-separated Identity codes, not necessarily
a field dictionary.

The policy does not convert authentication challenges/forbids, native protocol
responses, the intentional Buggy demonstrations or Stripe webhook acknowledgements.
A caller must handle those HTTP errors as well as `succeeded:false`.

## Route reference

`Bearer` means an authenticated JWT; product HTTP mutations additionally require
the Admin role. `Public` may still use the authenticated name or guest cookie.

| Method and path | Access | Input | Success envelope |
| --- | --- | --- | --- |
| GET `/products` | Public | Query parameters below | `PaginatedResponseListDto<ProductListDto>` |
| GET `/products/{id:int}` | Public | Route id | `DtoReturnOne<ProductDetailDto>` |
| GET `/products/filters` | Public | None | `DtoReturnOne<ProductFilterDto>` |
| POST `/products` | Admin | Multipart product form | `InsertReturn` |
| PUT `/products` | Admin | Multipart form including id | `UpdateReturn` |
| DELETE `/products/{id:int}` | Admin | Route id | `DeleteReturn` |
| GET `/basket` | Public | Buyer from JWT/cookie | `DtoReturnOne<BasketDto>` |
| POST `/basket` | Public | Query productId, quantity | `DtoReturnOne<BasketDto>` |
| DELETE `/basket` | Public | Query productId, quantity | `DtoReturnOne<BasketDto>` |
| POST `/account/login` | Public | JSON username, password | `DtoReturnOne<UserDto>` |
| POST `/account/register` | Public | JSON username, email, password | `InsertReturn` |
| GET `/account/currentUser` | Bearer | None | `DtoReturnOne<UserDto>` |
| GET `/account/savedAddress` | Bearer | None | `DtoReturnOne<UserAddressDto>` |
| GET `/orders` | Bearer | Nested paging and query inputs below | `PaginatedResponseListDto<OrderDto>` |
| GET `/orders/{id:int}` | Bearer | Route id | `DtoReturnOne<OrderDto>` |
| POST `/orders` | Bearer | JSON saveAddress, shippingAddress | `InsertReturn` |
| POST `/payments` | Bearer | No business input | `DtoReturnOne<BasketDto>` |
| POST `/payments/webhook` | Signed provider payload | Exact raw body and Stripe-Signature | Empty acknowledgement; see below |

Paths in this table are relative to `/api`. The multipart product update route is
`PUT /api/products`, not PUT to the detail URL.

## Catalog and product forms

Example query:

```text
/api/products?pageNumber=2&pageSize=6&fullSearchPhrase=run&brands=Nike,Puma&types=Shoes&orderBy=priceDesc
```

| Parameter | Behaviour |
| --- | --- |
| pageNumber | Default 1; values below 1 become 1 |
| pageSize | Default 6; nonpositive values use 6; maximum 50 |
| fullSearchPhrase | Case-insensitive name search; the old searchTerm name is not the query property |
| brands / types | Comma-separated values, case-insensitive; brand and type predicates are combined |
| orderBy | `price`, `priceDesc`, otherwise name order |

ProductListDto includes `id`, `name`, `price`, `pictureUrl`, `brand`, `type` and
`quantityInStock`. Only ProductDetailDto also includes `description`. Filters are
returned under `data: { brands: [...], types: [...] }`. Product detail misses
currently return a successful envelope with null data; the client adapter treats
that as a missing record rather than expecting HTTP 404.

Multipart create fields are name, description, price, brand, type,
quantityInStock and optional file. Update uses those fields plus id; omitting a
replacement file keeps the current image. The HTTP adapter chooses the editable
column list; callers do not control ChangeFields through this form. ProductControls
uses minimum price 100 and stock range 0–200. Product create validates required text;
the update control currently validates selected price/stock fields, not all required
text again. Prices and totals use integer minor units, not decimal-dollar strings.

BaseQuery also carries Filters/OrderInfo inputs. The kernel's expression engine is
covered independently, but the current UI does not submit an arbitrary grid and
arbitrary object-valued filter binding over HTTP needs consumer-level verification.
Do not infer a separate POST list/query route from those request classes.

## Account and basket

Login/currentUser data has `email`, `token`, `basket`. The basket can be null. Invalid
credentials are a failed HTTP 200 envelope; a protected request without a valid
token instead receives HTTP 401. Registration returns a key and does not sign in
automatically. JWTs contain the name/email/roles and expire after seven days.

The guest ID is kept in the `buyerId` cookie. Send credentials on browser requests.
Authenticated requests use the name from the token. Login transfers an existing
guest basket to the canonical account name and removes the previous account basket;
it does not add quantities from both baskets together. Cookie deletion follows
successful processing.

A missing guest/account basket and an absent saved address are normal successful
null results. A database read failure is a failed envelope, not an empty basket.
Basket items are added/removed with a positive quantity supplied in the query:

```text
POST /api/basket?productId=7&quantity=2
DELETE /api/basket?productId=7&quantity=2
```

Adding creates a guest ID/basket when necessary. Removing more than present clears
the line; removing from a missing basket does not create a row. Mutation results
carry the authoritative basket. Its data contains `id`, `buyerId`, `items`,
`paymentIntentId`, `clientSecret`; item data uses `productId` and product display
fields plus quantity. Saved-address data includes id and the address fields below.

## Orders and paging

Order list paging uses nested BaseQuery names, not Products' flat fields:

```text
/api/orders?PagingInfo.PageNumber=2&PagingInfo.PageRecordCount=25
```

Default page size is 25, maximum 200; nonpositive sizes use 25. Default order is
OrderDate descending then OrderId descending. `fullSearchPhrase` searches the
shipping full name. Generic OrderInfo uses entity columns, for example
`OrderInfo[0].ColumnName=OrderDate&OrderInfo[0].Ascending=false`. All criteria are
combined with authenticated buyer ownership. Missing/foreign detail IDs produce
the same failed envelope.

An order is created from the authenticated buyer's current basket:

```json
{
  "saveAddress": true,
  "shippingAddress": {
    "fullName": "Example Buyer",
    "address1": "Example Street",
    "address2": "Unit 2",
    "city": "Example City",
    "state": "Example State",
    "zip": "12345",
    "country": "Example Country"
  }
}
```

The response key is the order number. Stock decrement, order creation, basket
removal and optional address save are one database transaction. OrderDto contains
id, buyerId, shippingAddress, orderDate, orderItems, subTotal, deliveryFee, total and
the status string (`Pending`, `PaymentReceived`, `PaymentFailed`). Item name/image/
price are checkout snapshots. Delivery is 500 at subtotal <= 10000, otherwise 0.

## Webhook

POST `/api/payments` computes the amount from the authenticated basket, creates or
updates the Stripe intent and returns the basket with its client secret. It does
not confirm a card payment itself. The browser currently confirms through Stripe
before submitting POST orders.

POST `/api/payments/webhook` expects an exact raw provider payload and
`Stripe-Signature` header. The adapter uses Stripe.net 41.15.0's ConstructEvent with
API-version checking enabled (the SDK default is 2022-11-15). Verify webhook endpoint
version compatibility during Stripe test-mode acceptance.

| Condition | HTTP response |
| --- | --- |
| Verified unrelated object or pending charge | 200, empty body |
| Verified terminal charge, no matching order | 200, empty body |
| Matching order updated, or duplicate delivery | 200, empty body |
| Invalid signature/payload rejected by the verifier | 400 |
| Missing signing configuration or processing/database failure | 500, retryable |

Succeeded/failed Charge states map to PaymentReceived/PaymentFailed. A late failure
does not downgrade a paid order. Webhook responses are not browser envelopes. The
[architecture limitations](architecture.md#payments-and-webhook) explain the gap
when an event precedes order creation and the absence of durable reconciliation.

## Intentional error demonstrations

These GET endpoints remain available for the frontend About/error screens:

| Path | Response |
| --- | --- |
| `/api/buggy/bad-request` | HTTP 400 ProblemDetails |
| `/api/buggy/validation-error` | HTTP 400 with field errors Problem1/Problem2 |
| `/api/buggy/unauthorized` | HTTP 401 |
| `/api/buggy/not-found` | HTTP 404 |
| `/api/buggy/server-error` | Deliberately thrown exception, HTTP 500 ProblemDetails |

They do not opt into EnvelopeResponseAttribute. The client preserves the validation
message list, feedback for HTTP errors and navigation to the server-error page.

## MCP product tools

Use the MCP protocol at `/mcp`, not the `/api` prefix. The implemented/discovered
names are `mcp_test`, `list_products`, `get_product`, `get_product_filters`,
`create_product`, `update_product`, `delete_product`.

The six business tools dispatch the same Application requests and return the same
envelopes as the HTTP product operations. Create/update accept ordinary product
arguments without file upload; HTTP multipart remains the image-upload adapter.
The list tool's paging defaults to 10 and clamps pageSize to 1–50; HTTP catalog
defaults to 6. The test tool returns a plain greeting string.

The transport is currently anonymous and no controller Admin policy is inherited
by the tools. See [authorization boundaries](architecture.md#authorization-boundaries).
Documentation verification negotiated an anonymous MCP session and listed tools;
it did not invoke mutations or create/delete any product.
