# saleserp

ECNESOFT Field Sales

Sydney-focused B2B field sales management demo for authenticated sales reps and administrators.

Login:

- ID: `Ecnesoft`
- PW: `Ecnesoft`

Run:

```powershell
$env:DOTNET_CLI_HOME='C:\Users\user\OneDrive\Desktop\.dotnet-home'
dotnet run
```

Included:

- HTTP-only cookie authentication and manual HMAC JWT bearer authentication
- Role authorization for `ADMIN` and `SALES`
- Google Maps JavaScript API integration with internal-map fallback
- ABN checksum validation and mocked ABR lookup-backed CSV/XLSX bulk import
- Safe sales-note image upload under `wwwroot/uploads/sales-notes`
- Customer coordinate correction endpoint for drag-and-drop map pins
- Haversine-radius prospect recommendation endpoint
- Admin postcode/suburb penetration dashboard API
- SQLite schema and frontend Google Maps integration guide under `Docs`

Important production changes:

- Replace the demo JWT signing key in `appsettings.json`.
- Add a browser-restricted Google Maps API key under `GoogleMaps:ApiKey` to enable real Google Maps.
- Use HTTPS and set auth cookies to `CookieSecurePolicy.Always`.
- Add CSRF tokens if cookie auth is used for browser write requests.
- Replace `InMemorySalesRepository` with EF Core or ADO.NET backed by `Docs/schema.sql`.
- Replace `MockAbrLookupClient` with the approved ABR Lookup integration and API key handling.

Google Maps setup:

```json
"GoogleMaps": {
  "ApiKey": "YOUR_BROWSER_RESTRICTED_GOOGLE_MAPS_KEY",
  "MapId": "YOUR_MAP_ID_OR_DEMO_MAP_ID"
}
```

You can also use environment variables:

```powershell
$env:GoogleMaps__ApiKey="YOUR_BROWSER_RESTRICTED_GOOGLE_MAPS_KEY"
$env:GoogleMaps__MapId="DEMO_MAP_ID"
```

Enable Maps JavaScript API in Google Cloud Console and restrict the key by HTTP referrer. For production Advanced Markers, create a JavaScript map ID and replace `DEMO_MAP_ID`.
