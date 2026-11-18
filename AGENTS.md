# ReStore agent instructions

## Storefront UI

- Keep new and redesigned presentational components independent of React Router and Redux so they can move to a future Next.js storefront. They must not import `react-router-dom`, `react-redux`, Redux store hooks/slices, or wrappers around those dependencies. Keep API requests and browser session access out of these components as well.
- Pass data, loading and error states, URLs, and event handlers as typed props. Use semantic links with `href`; when client-side navigation is needed, inject a link adapter from the page layer. Route components and container hooks own navigation, API calls, and store selection or dispatch. Local state for UI behavior is fine in a view.
- Use the existing MUI theme and reusable components for the redesign. See the [storefront redesign plan](docs/storefront-redesign-plan.md) for scope and validation. SEO, Next.js, and changes to the deployment model are deferred to a separate phase.
