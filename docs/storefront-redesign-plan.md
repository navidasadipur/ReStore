# ReStore storefront redesign

Status: implemented in the React client. The MUI storefront uses the selected
layout and deep green palette. The original rollout sequence is recorded in the
[implementation prompts](storefront-redesign-prompts.md).

## 1. Selected design

Three browser prototypes were compared at mobile and desktop widths: compact,
spacious and inspired by [Snapp Shop](https://snappshop.ir/). The third layout
was selected, with the purple palette to be replaced. Use ReStore branding and
catalog images.

| Area | Design decision |
| --- | --- |
| Header | Make search prominent. Keep account and basket actions within reach. Place search on its own row on mobile. |
| Home | Put four illustrated category shortcuts above a short banner. Follow with a compact section of selected products using real catalog data. |
| Catalog | Use a consistent product grid with readable prices and a filter sidebar on desktop. Open the same filters in a drawer on mobile. |
| Product | Group price and availability close to the purchase action. Give the product image and name room to breathe. |

The light theme uses deep green (`#224938`) for brand and primary actions,
off-white (`#F8F8F5`) for the page, and white (`#FFFFFF`) for cards. The dark
theme uses lighter green for the primary action. The client bundles the licensed
Vazirmatn variable font. Reserve a muted red accent for discount labels when
discount data is available.

The prototype's original prices and discount percentages were illustrative. The
current product API supplies one price in rials. Show that amount in tomans via
the existing formatter. Use a neutral label such as **محصولات منتخب** on home.
Show previous price, percentage off and sale labels once an agreed API contract
provides real discount data.

## 2. Build the common storefront UI

- Set Persian language and RTL direction for the storefront and wire MUI's RTL
  setup, including overlays rendered in portals. Review shared header, basket and
  checkout when changing direction at the document level. Use a licensed local
  Persian font and test mixed Persian/Latin names and numbers.
- Extract a shared MUI theme from `client/src/app/layout/App.tsx`. Define roles
  for accent, surface, text and borders, plus typography, spacing, radius and
  responsive layouts. Use theme overrides for shared behavior and `sx` for local
  layout. Check contrast and focus in the existing theme modes if both remain visible.
- Build presentation components such as `StorefrontHeader`, `ProductCard`,
  `Price`, and `CatalogFilters`. Pass typed data, URLs, actions and loading state
  as props. Page containers own React Router, Redux and API calls, as required by
  [AGENTS.md](../AGENTS.md). A product card should render with props alone and no
  Router or Redux provider. `Price` uses the existing rial-to-toman formatter.

## 3. Replace screens in small slices

| Order | Screen | Scope and completion criteria |
| --- | --- | --- |
| 1 | Header and one product card | Persian search, brand, account and basket count. Wire search to the existing catalog and keep the current account and basket flow. Verify one real product card at mobile and desktop widths before expanding. |
| 2 | Catalog | Two columns on typical phones and more as room permits; allow a single column on narrow layouts if required. Use an inline sidebar on desktop and an accessible filter drawer on mobile. Reuse current brand/type filters, sort, pagination and API query adapter. Cover loading, empty, error and retry states. Reset to the first page when search or filters change. |
| 3 | Product detail | Real image, name, description, toman price, availability and purchase action. Preserve the existing product URL and cart behavior. Cover missing images, long names and out of stock products. |
| 4 | Home | Replace the demo welcome and rotating hero with illustrated category shortcuts, one compact banner and a selected products section, in that order. Use the existing type filters and real catalog data. |

The [catalog API](api-contracts.md) exposes types and brands; use these before
considering a dedicated category model. Leave discounts, ratings, multi-image
galleries and marketplace features for phases that include the required data.
Keep the current API contracts, existing URLs, deployment model and shopping
flows. Basket, checkout, account and admin get compatibility checks for shared
styles, with screen redesigns left for later.

The catalog address carries search (`q`), repeated `type` and `brand` filters,
`sort` and `page`. The client translates these fields through the API query
adapter. The storefront and inventory each manage their own list state.

## 4. Verify the release candidate

- Check mobile and desktop with real data: search, category links, filters,
  pagination, product detail, and adding an item to the basket. Smoke-test guest
  basket, sign-in, admin navigation and checkout through the existing sandbox
  flow, including payment return when a sandbox is available.
- Check keyboard navigation and visible focus. Make sure the filter drawer can
  close with Escape and returns focus to its trigger. Check no horizontal overflow
  with long Persian/Latin names, image placeholders, and readable price labels.
- Run the existing [client type checks, tests and isolated production build](../README.md#build-and-verify).
  Compare mobile load behavior against the current storefront on the same profile,
  especially image requests and layout shifts. Reserve image space, and lazy-load
  product images below the first screen.

The implementation passed the client type check, 27 tests and the isolated
production build. Chrome checks at 390 px and 1440 px used the local API with
18 seeded products. They covered category navigation, search, sorting, paging,
the filter drawer and keyboard focus, product details, the guest basket, admin
navigation and checkout through order review. A separate HTTP integration test
passed with simulated ZarinPal responses. Live gateway and comparative mobile
performance measurements remain release checks.

## Later phase: SEO and Next.js

Revisit Next.js when organic search becomes a release goal. Evaluate delivering
the public home, category and product content in the initial HTML, keeping product
URLs stable and defining metadata, sitemap, structured product data and rules for
filtered URLs. Review price and inventory freshness and the hosting impact before
selecting a rendering strategy. The view boundary in this plan lets these pages
reuse the UI while changing the routing and data layer. This phase requires no
Next.js or Vite migration, search-engine metadata rollout, or API redesign.
