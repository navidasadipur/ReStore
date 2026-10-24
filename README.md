# ReStore
e-commerce store with dot net and React Redux

View a demo of this app here:

<a href="https://Restore.navidasadipur.ir">https://Restore.navidasadipur.ir</a>

<table>
<tr>
    <td colspan="2">test username and password</td>
  </tr>
  <tr>
    <th>username</th>
    <th>password</th>
  </tr>
  <tr>
    <td>admin</td>
    <td>Pa$$w0rd</td>
  </tr>
  <tr>
    <td>alice</td>
    <td>Pa$$w0rd</td>
  </tr>
</table>




API using:

Building the API locally requires the .NET 9 SDK:

```sh
dotnet restore API/API.csproj
dotnet build API/API.csproj --configuration Release
```

The Dockerfile builds and runs the API with .NET 9 images. Deployment tools
under `tools/` continue to target .NET 8 independently.

### MCP products endpoint

The API exposes an unauthenticated MCP endpoint at `/mcp`. The first version
provides these product tools: `list_products`, `get_product`,
`get_product_filters`, `create_product`, `update_product`, and
`delete_product`. Product creation and updates do not upload images; updates
keep the product's current image.

### Health checks

`GET /health/live` checks that the API process is running. `GET /health/ready`
also checks that `StoreContext` can connect to PostgreSQL. Both endpoints return
JSON and are available without authentication.

<ul>
<li>.NET 9</li>
<li>ASPNET Core Identity(JWT type)</li>
<li>AutoMapper</li>
</ul>

  <br />
frontend using:
<ul>
<li>react + typescript</li>
<li>Redux</li>
<li>Material UI</li>
<li>axios</li>
<li>React-Router</li>
<li>react-toastify</li>
<li>react-hook-form + Yup</li>
</ul>


I am learning from Neil Cummings in the course <a href="https://www.udemy.com/course/learn-to-build-an-e-commerce-store-with-dotnet-react-redux/">learn to build an e-commerce store with dotnet react redux</a>
