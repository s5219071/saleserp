# Frontend Google Maps Integration Guide

The app now attempts to load Google Maps after login. If `GoogleMaps:ApiKey` is empty or the script fails to load, it falls back to the internal Australia map.

References:

- [Advanced Markers reference](https://developers.google.com/maps/documentation/javascript/reference/advanced-markers)
- [Draggable Advanced Markers](https://developers.google.com/maps/documentation/javascript/advanced-markers/draggable-markers)
- [Markers legacy guide](https://developers.google.com/maps/documentation/javascript/markers)

## Authenticated API Calls

JWT header style:

```ts
const token = localStorage.getItem("ecnesoft.sales.token");

const customers = await fetch("/api/customers?customerType=PROSPECT", {
  headers: {
    Authorization: `Bearer ${token}`
  }
}).then(r => r.json());
```

## App Configuration

```json
"GoogleMaps": {
  "ApiKey": "YOUR_BROWSER_RESTRICTED_GOOGLE_MAPS_KEY",
  "MapId": "YOUR_MAP_ID_OR_DEMO_MAP_ID"
}
```

For local testing:

```powershell
$env:GoogleMaps__ApiKey="YOUR_BROWSER_RESTRICTED_GOOGLE_MAPS_KEY"
$env:GoogleMaps__MapId="DEMO_MAP_ID"
dotnet run
```

The API key is delivered to authenticated users through `/api/config/maps`. Google Maps browser keys are not true server secrets, so restrict them in Google Cloud Console by HTTP referrer and enable only the required APIs.

HTTP-only cookie style:

```ts
const customers = await fetch("/api/customers", {
  credentials: "include"
}).then(r => r.json());
```

For the highest browser security, prefer HTTP-only cookies with CSRF protection for same-site web apps. JWT bearer tokens are convenient for mobile apps and cross-domain APIs, but must not be stored in long-lived, readable browser storage in high-risk deployments.

## Marker Color Mapping

```ts
const markerColors: Record<string, string> = {
  ACTIVE: "#16A34A",
  OPEN: "#DC2626",
  TERMINATION: "#F59E0B"
};

function colorForCustomer(customer: any) {
  return markerColors[customer.prospectStatus ?? "ACTIVE"];
}
```

## Google Maps Advanced Marker Rendering

```ts
async function initGoogleSalesMap(customers: any[]) {
  const { Map } = await google.maps.importLibrary("maps") as google.maps.MapsLibrary;
  const { AdvancedMarkerElement, PinElement } =
    await google.maps.importLibrary("marker") as google.maps.MarkerLibrary;

  const map = new Map(document.getElementById("map") as HTMLElement, {
    center: { lat: -33.8688, lng: 151.2093 },
    zoom: 11,
    mapId: "YOUR_GOOGLE_MAP_ID"
  });

  customers.forEach(customer => {
    const pin = new PinElement({
      background: colorForCustomer(customer),
      borderColor: "#111827",
      glyphColor: "#FFFFFF"
    });

    const marker = new AdvancedMarkerElement({
      map,
      position: { lat: customer.latitude, lng: customer.longitude },
      title: customer.companyName,
      content: pin.element,
      gmpDraggable: true
    });

    marker.addListener("dragend", async () => {
      const position = marker.position as google.maps.LatLng;
      await fetch(`/api/customers/${customer.id}/coordinates`, {
        method: "PATCH",
        credentials: "include",
        headers: {
          "Content-Type": "application/json",
          Authorization: `Bearer ${localStorage.getItem("ecnesoft.sales.token") ?? ""}`
        },
        body: JSON.stringify({
          latitude: position.lat(),
          longitude: position.lng()
        })
      });
    });
  });
}
```

## Dashboard Heatmap JSON Shape

The production dashboard can render postcode-level heatmap intensity from `/api/admin/dashboard/penetration`.

```json
[
  {
    "postcode": "2135",
    "suburb": "Strathfield",
    "currentCustomers": 2,
    "prospectStores": 8,
    "totalStores": 10,
    "penetrationRate": 20.0,
    "heatmapWeight": 0.8,
    "centroidLatitude": -33.8719,
    "centroidLongitude": 151.0945
  }
]
```
