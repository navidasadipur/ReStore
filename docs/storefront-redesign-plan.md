# ReStore storefront redesign

Status: planned. This phase improves the Persian, mobile-first storefront using the
existing React, TypeScript and MUI 5 stack. The visual reference is Digikala's
prominent search, category shortcuts and consistent product cards. ReStore uses
its own colors, assets and layout density, sized for a small shop that can grow.

## 1. Compare two directions, then choose one

Timebox the design exploration to half a day. In Figma, sketch two directions
with the same logo, Persian text and representative catalog data:

| Direction | Layout |
| --- | --- |
| Compact storefront | Search first, short category strip, compact hero and visible products early. |
| More spacious storefront | Search still prominent, larger feature banner, more room for category and product cards. |

Sketch the header and first screen of home and catalog at mobile (about 390 px)
and desktop (about 1440 px). Include a long product name, a real price in tomans,
and the filter entry point. Start from the compact direction; prefer the other
only if search and category discovery stay as clear. Pick one direction based on
how easily a shopper can find search, browse a category and see products on mobile.
Sketch the product detail layout only for the chosen direction. If a Figma file
would slow this step down, compare the same rough layouts on paper or in a quick
browser prototype. Build the chosen direction in MUI with real data next; skip
designing every screen and state in Figma before touching code.

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
| 4 | Home | Replace the demo welcome and rotating hero with one banner, category shortcuts and one product section. Use the existing type filters for shortcuts and real catalog data for the product section. Keep the path from home to a category or product short. |

The [catalog API](api-contracts.md) exposes types and brands; use these before
considering a dedicated category model. Leave discounts, ratings, multi-image
galleries and marketplace features for phases that include the required data.
Keep the current API contracts, existing URLs, deployment model and shopping
flows. Basket, checkout, account and admin get compatibility checks for shared
styles, with screen redesigns left for later.

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

## Later phase: SEO and Next.js

Revisit Next.js when organic search becomes a release goal. Evaluate delivering
the public home, category and product content in the initial HTML, keeping product
URLs stable and defining metadata, sitemap, structured product data and rules for
filtered URLs. Review price and inventory freshness and the hosting impact before
selecting a rendering strategy. The view boundary in this plan lets these pages
reuse the UI while changing the routing and data layer. This phase requires no
Next.js or Vite migration, search-engine metadata rollout, or API redesign.
