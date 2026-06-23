const state = {
  token: localStorage.getItem("ecnesoft.sales.token") || "",
  user: null,
  customers: [],
  happyGroups: [],
  posts: [],
  selectedCustomerId: null,
  editingCustomerId: null,
  editingPostId: null,
  dragging: null,
  map: {
    zoom: 1,
    centerX: 50,
    centerY: 50,
    tx: 0,
    ty: 0,
    panning: null
  },
  google: {
    enabled: false,
    map: null,
    infoWindow: null,
    AdvancedMarkerElement: null,
    previewMarker: null,
    markers: new globalThis.Map()
  },
  filters: {
    types: new Set(),
    date: "",
    competitors: new Set(),
    happyGroupId: ""
  },
  customerFilters: {
    types: new Set()
  },
  happy: {
    selectedIds: new Set(),
    page: 1,
    pageSize: 20,
    type: ""
  },
  addressPreviewTimer: null,
  lastAddressPreviewKey: ""
};

const bounds = {
  north: -9.0,
  south: -44.5,
  west: 111.0,
  east: 155.5
};

const defaultGoogleView = {
  center: { lat: -33.8688, lng: 151.2093 },
  zoom: 11
};

const typeMeta = {
  ACTIVE: { label: "Active", colorName: "green", color: "#16a34a", letter: "A" },
  TERMINATION: { label: "Termination", colorName: "orange", color: "#f97316", letter: "T" },
  CLOSED: { label: "Closed", colorName: "red", color: "#dc2626", letter: "C" },
  PROSPECT: { label: "Prospect", colorName: "blue", color: "#2563eb", letter: "P" },
  OWNERSHIP: { label: "Ownership", colorName: "teal", color: "#0f766e", letter: "O" }
};

const competitorMeta = {
  KPOS: { label: "Kpos", colorName: "yellow", color: "#eab308", letter: "K" },
  ORDERNOW: { label: "OrderNow", colorName: "purple", color: "#9333ea", letter: "O" },
  QONUS: { label: "Qonus", colorName: "sky blue", color: "#38bdf8", letter: "Q" },
  SQUARE: { label: "Square", colorName: "beige", color: "#d6c4a4", letter: "S" },
  ETC: { label: "ETC", colorName: "lime", color: "#84cc16", letter: "E" }
};

const dataOptions = [
  { value: "", label: "No" },
  { value: "1m", label: "Recent 1 month" },
  { value: "3m", label: "Recent 3 months" },
  { value: "6m", label: "Recent 6 months" },
  { value: "1y", label: "Past 1 year" },
  { value: "all", label: "Past customers" }
];

const customerXmlTemplateFields = [
  { key: "companyName", label: "Company", required: true, sample: "Sample Store" },
  { key: "type", label: "Type", required: true, sample: "PROSPECT", hint: "ACTIVE, TERMINATION, CLOSED, PROSPECT, OWNERSHIP" },
  { key: "terminationDate", label: "Termination Date", required: false, sample: "", hint: "YYYY-MM-DD, required only for TERMINATION" },
  { key: "terminationReason", label: "Termination Reason", required: false, sample: "", hint: "Required only for TERMINATION, max 100 chars" },
  { key: "competitor", label: "Competitor", required: false, sample: "", hint: "KPOS, ORDERNOW, QONUS, SQUARE, ETC" },
  { key: "abn", label: "ABN", required: false, sample: "" },
  { key: "address", label: "Address", required: true, sample: "14/20-30 Stubbs St" },
  { key: "city", label: "Suburb", required: true, sample: "Silverwater" },
  { key: "state", label: "State", required: true, sample: "NSW" },
  { key: "postcode", label: "Postcode", required: true, sample: "2128" },
  { key: "latitude", label: "Latitude", required: true, sample: "-33.8353852" },
  { key: "longitude", label: "Longitude", required: true, sample: "151.0398518" },
  { key: "generalNote", label: "Note", required: false, sample: "", hint: "Max 200 chars" }
];

const customerImportAliases = new Map(customerXmlTemplateFields.flatMap((field) => [
  [normalizeImportKey(field.key), field.key],
  [normalizeImportKey(field.label), field.key]
]).concat([
  ["companyname", "companyName"],
  ["company", "companyName"],
  ["suburb", "city"],
  ["city", "city"],
  ["note", "generalNote"],
  ["generalnote", "generalNote"],
  ["terminationdate", "terminationDate"],
  ["terminationreason", "terminationReason"],
  ["lng", "longitude"],
  ["lon", "longitude"],
  ["lat", "latitude"]
]));

const $ = (selector) => document.querySelector(selector);

document.addEventListener("DOMContentLoaded", () => {
  renderStaticFilters();
  bindEvents();
  resumeSession();
});

function bindEvents() {
  $("#loginForm").addEventListener("submit", login);
  $("#logoutButton").addEventListener("click", logout);
  $("#openNewButton").addEventListener("click", openNewCustomerModal);
  $("#newCustomerForm").addEventListener("submit", createCustomer);
  $("#newCustomerType").addEventListener("change", toggleTerminationFields);
  ["address", "city", "state", "postcode"].forEach((name) => {
    $("#newCustomerForm").elements[name].addEventListener("blur", syncAddressFieldsFromCurrentForm);
  });
  ["calculatedLatitude", "calculatedLongitude"].forEach((name) => {
    $("#newCustomerForm").elements[name].addEventListener("input", scheduleCoordinatePreview);
    $("#newCustomerForm").elements[name].addEventListener("blur", previewManualCoordinates);
  });
  $("#closeNewCustomerButton").addEventListener("click", closeNewCustomerModal);
  $("#cancelNewCustomerButton").addEventListener("click", closeNewCustomerModal);
  $("#clearMapFiltersButton").addEventListener("click", clearMapFilters);
  $("#happyVisitFilter").addEventListener("change", onHappyVisitFilterChange);
  $("#happyTypeSelect").addEventListener("change", onHappyTypeChange);
  $("#happyPrevButton").addEventListener("click", () => changeHappyPage(-1));
  $("#happyNextButton").addEventListener("click", () => changeHappyPage(1));
  $("#happyAddSelectedButton").addEventListener("click", addHappySelectedCustomers);
  $("#happyCancelButton").addEventListener("click", resetHappyEditor);
  $("#happySaveButton").addEventListener("click", saveHappyGroup);
  $("#addPostButton").addEventListener("click", showPostForm);
  $("#backPostButton").addEventListener("click", backFromPostForm);
  $("#postForm").addEventListener("submit", savePost);
  $("#exportCustomerTemplateButton").addEventListener("click", exportCustomerXmlTemplate);
  $("#importCustomerXmlInput").addEventListener("change", importCustomerXml);
  $("#zoomInButton").addEventListener("click", zoomIn);
  $("#zoomOutButton").addEventListener("click", zoomOut);
  $("#resetMapButton").addEventListener("click", resetMap);
  $("#mapCanvas").addEventListener("pointerdown", startPan);
  $("#mapCanvas").addEventListener("wheel", onMapWheel, { passive: false });

  document.querySelectorAll(".nav-button").forEach((button) => {
    button.addEventListener("click", () => showView(button.dataset.view));
  });

  window.addEventListener("pointermove", onPointerMove);
  window.addEventListener("pointerup", onPointerUp);
  window.addEventListener("resize", updateMapTransform);
}

function renderStaticFilters() {
  renderTypeChecks($("#mapTypeFilters"), state.filters.types, () => {
    renderAll();
    resetGoogleMapToSydney();
  });
  renderTypeChecks($("#customerTypeFilters"), state.customerFilters.types, () => {
    renderCustomerTable();
  });

  const dateContainer = $("#dateFilters");
  dateContainer.replaceChildren();
  dataOptions.forEach((option) => {
    const label = document.createElement("label");
    label.className = "check-row";
    const input = document.createElement("input");
    input.type = "radio";
    input.name = "dateFilter";
    input.value = option.value;
    input.addEventListener("change", () => {
      state.filters.date = input.checked ? option.value : "";
      renderAll();
      resetGoogleMapToSydney();
    });
    label.append(input, document.createTextNode(option.label));
    dateContainer.append(label);
  });

  const competitorContainer = $("#competitorFilters");
  competitorContainer.replaceChildren();
  Object.entries(competitorMeta).forEach(([value, meta]) => {
    const label = document.createElement("label");
    label.className = "check-row";
    const input = document.createElement("input");
    input.type = "checkbox";
    input.value = value;
    input.addEventListener("change", () => {
      updateSet(state.filters.competitors, value, input.checked);
      renderAll();
      resetGoogleMapToSydney();
    });
    const swatch = colorSwatch(meta.color);
    label.append(input, document.createTextNode(`${meta.label} - ${meta.colorName} `), swatch);
    competitorContainer.append(label);
  });
}

function renderTypeChecks(container, targetSet, onChange) {
  container.replaceChildren();
  Object.entries(typeMeta).forEach(([value, meta]) => {
    const label = document.createElement("label");
    label.className = "check-row";
    const input = document.createElement("input");
    input.type = "checkbox";
    input.value = value;
    input.addEventListener("change", () => {
      updateSet(targetSet, value, input.checked);
      onChange();
    });
    label.append(input, document.createTextNode(`${meta.label} - ${meta.colorName} `), colorSwatch(meta.color));
    container.append(label);
  });
}

function colorSwatch(color) {
  const swatch = document.createElement("span");
  swatch.className = "filter-swatch";
  swatch.style.background = color;
  return swatch;
}

async function resumeSession() {
  if (!state.token) {
    showLogin();
    return;
  }

  try {
    state.user = await api("/api/auth/me");
    await enterApp();
  } catch {
    localStorage.removeItem("ecnesoft.sales.token");
    state.token = "";
    showLogin();
  }
}

async function login(event) {
  event.preventDefault();
  $("#loginError").textContent = "";

  try {
    const response = await fetch("/api/auth/login", {
      method: "POST",
      credentials: "include",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        email: $("#loginEmail").value.trim(),
        password: $("#loginPassword").value,
        useCookie: true
      })
    });

    if (!response.ok) {
      throw new Error("Login failed");
    }

    const data = await response.json();
    state.token = data.token;
    state.user = {
      id: data.id,
      email: data.email,
      fullName: data.fullName,
      role: data.role
    };
    localStorage.setItem("ecnesoft.sales.token", state.token);
    await enterApp();
  } catch {
    $("#loginError").textContent = "Please check the login ID and password.";
  }
}

async function logout() {
  try {
    await api("/api/auth/logout", { method: "POST" });
  } finally {
    localStorage.removeItem("ecnesoft.sales.token");
    state.token = "";
    state.user = null;
    showLogin();
  }
}

async function enterApp() {
  $("#loginView").hidden = true;
  $("#appView").hidden = false;
  $("#userName").textContent = state.user.fullName;
  $("#userRole").textContent = state.user.role;

  await initializeMapProvider();
  await Promise.all([loadCustomers(), loadHappyGroups(), loadPosts()]);
  renderAll();
  if (!state.google.enabled) {
    positionMapLabels();
    updateMapTransform();
  }
}

function showLogin() {
  $("#loginView").hidden = false;
  $("#appView").hidden = true;
}

function showView(viewId) {
  document.querySelectorAll(".view").forEach((view) => view.classList.remove("active"));
  document.querySelectorAll(".nav-button").forEach((button) => button.classList.remove("active"));
  $(`#${viewId}`).classList.add("active");
  document.querySelector(`[data-view="${viewId}"]`)?.classList.add("active");

  if (viewId === "happyVisitView") {
    renderHappyVisit();
  }
  if (viewId === "dashboardView") {
    renderPosts();
  }
  if (viewId === "customerView") {
    renderCustomerTable();
  }
}

async function api(url, options = {}) {
  const headers = new Headers(options.headers || {});
  if (!(options.body instanceof FormData) && options.body && !headers.has("Content-Type")) {
    headers.set("Content-Type", "application/json");
  }
  if (state.token) {
    headers.set("Authorization", `Bearer ${state.token}`);
  }

  const response = await fetch(url, {
    ...options,
    credentials: "include",
    headers
  });

  if (response.status === 401) {
    throw new Error("Please login again.");
  }
  if (response.status === 403) {
    throw new Error("Access denied.");
  }
  if (!response.ok) {
    let message = response.statusText;
    try {
      const problem = await response.json();
      message = flattenProblem(problem) || message;
    } catch {
      message = await response.text();
    }
    throw new Error(message || "Request failed.");
  }
  if (response.status === 204) {
    return null;
  }

  return response.json();
}

function flattenProblem(problem) {
  if (problem?.errors) {
    return Object.values(problem.errors).flat().join(" ");
  }
  return problem?.detail || problem?.message || problem?.title || "";
}

async function initializeMapProvider() {
  try {
    const config = await api("/api/config/maps");
    if (!config.apiKey) {
      useInternalMap("Internal map - add GoogleMaps:ApiKey to enable Google Maps");
      return;
    }

    await loadGoogleMapsScript(config);
    const [{ Map: GoogleMap, InfoWindow }, { AdvancedMarkerElement }] = await Promise.all([
      google.maps.importLibrary("maps"),
      google.maps.importLibrary("marker")
    ]);

    state.google.enabled = true;
    state.google.AdvancedMarkerElement = AdvancedMarkerElement;
    state.google.infoWindow = new InfoWindow();
    state.google.map = new GoogleMap($("#googleMapCanvas"), {
      center: defaultGoogleView.center,
      zoom: defaultGoogleView.zoom,
      mapId: config.mapId || "DEMO_MAP_ID",
      mapTypeControl: true,
      streetViewControl: true,
      fullscreenControl: true,
      gestureHandling: "greedy",
      restriction: {
        latLngBounds: {
          north: -8.0,
          south: -45.5,
          west: 110.0,
          east: 156.8
        },
        strictBounds: false
      }
    });

    state.google.map.addListener("zoom_changed", updateGoogleZoomLabel);
    $("#googleMapCanvas").hidden = false;
    $("#mapCanvas").hidden = true;
    $("#mapProviderStatus").textContent = "Google Maps";
    updateGoogleZoomLabel();
  } catch (error) {
    console.warn("Google Maps could not be loaded.", error);
    useInternalMap("Internal map - Google Maps failed to load");
  }
}

function loadGoogleMapsScript(config) {
  if (globalThis.google?.maps?.importLibrary) {
    return Promise.resolve();
  }

  if (globalThis.__ecnesoftGoogleMapsPromise) {
    return globalThis.__ecnesoftGoogleMapsPromise;
  }

  globalThis.__ecnesoftGoogleMapsPromise = new Promise((resolve, reject) => {
    const callbackName = "__ecnesoftGoogleMapsReady";
    globalThis[callbackName] = () => resolve();

    const params = new URLSearchParams({
      key: config.apiKey,
      loading: "async",
      callback: callbackName,
      v: "weekly",
      region: config.region || "AU",
      language: config.language || "en"
    });

    if (config.mapId) {
      params.set("map_ids", config.mapId);
    }

    const script = document.createElement("script");
    script.src = `https://maps.googleapis.com/maps/api/js?${params.toString()}`;
    script.async = true;
    script.defer = true;
    script.onerror = () => reject(new Error("Google Maps script load failed."));
    document.head.append(script);
  });

  return globalThis.__ecnesoftGoogleMapsPromise;
}

function useInternalMap(message) {
  state.google.enabled = false;
  state.google.map = null;
  state.google.markers.clear();
  $("#googleMapCanvas").hidden = true;
  $("#mapCanvas").hidden = false;
  $("#mapProviderStatus").textContent = message;
}

async function loadCustomers() {
  state.customers = await api("/api/customers");
  setStatus(`${state.customers.length} customers loaded`);
}

async function loadHappyGroups() {
  state.happyGroups = await api("/api/happy-visits");
}

async function loadPosts() {
  state.posts = await api("/api/dashboard/posts");
}

function renderAll() {
  renderSummary();
  renderFilterCustomerList();
  renderPins(true);
  renderHappyVisitSelect();
  renderHappyVisit();
  renderPosts();
  renderCustomerTable();
}

function renderSummary() {
  const now = Date.now();
  const twoWeeksAgo = now - 14 * 24 * 60 * 60 * 1000;
  ["ACTIVE", "TERMINATION", "CLOSED", "PROSPECT"].forEach((type) => {
    const count = state.customers.filter((customer) => customer.type === type).length;
    const delta = state.customers.filter((customer) => customer.type === type && Date.parse(customer.createdAt) >= twoWeeksAgo).length;
    const suffix = titleCase(type);
    $(`#metric${suffix}`).textContent = count;
    $(`#metric${suffix}Delta`).textContent = `+${delta}`;
  });
}

function renderFilterCustomerList() {
  const list = $("#filterCustomerList");
  list.replaceChildren();
  const customers = filteredCustomersForMap();
  customers.forEach((customer) => {
    list.append(customerRow(customer));
  });
}

function renderCustomerTable() {
  const table = $("#customerTable");
  table.replaceChildren();
  const selectedTypes = state.customerFilters.types;
  const rows = selectedTypes.size === 0
    ? []
    : state.customers.filter((customer) => selectedTypes.has(customer.type));

  if (rows.length === 0) {
    const empty = document.createElement("div");
    empty.className = "empty-state";
    empty.textContent = "Select one or more filter options to show customers.";
    table.append(empty);
    return;
  }

  rows.forEach((customer) => {
    const row = document.createElement("article");
    row.className = "customer-card";
    const details = document.createElement("div");
    details.className = "customer-card-details";
    details.innerHTML = `
      <strong>${escapeHtml(customer.companyName)}</strong>
      <span>${escapeHtml(customer.address)}, ${escapeHtml(customer.city)} ${escapeHtml(customer.postcode)}</span>
      <small>${escapeHtml(typeMeta[customer.type]?.label || customer.type)}${customer.competitor ? ` | ${escapeHtml(competitorLabel(customer.competitor))}` : ""}</small>
    `;

    const actions = document.createElement("div");
    actions.className = "customer-card-actions";
    const viewButton = document.createElement("button");
    viewButton.type = "button";
    viewButton.className = "light-button";
    viewButton.textContent = "View";
    viewButton.addEventListener("click", () => {
      showView("mapView");
      selectCustomer(customer.id);
    });

    const editButton = document.createElement("button");
    editButton.type = "button";
    editButton.textContent = "Edit";
    editButton.addEventListener("click", () => openEditCustomerModal(customer.id));

    actions.append(viewButton, editButton);
    row.append(details, actions);
    table.append(row);
  });
}

function customerRow(customer) {
  const row = document.createElement("div");
  row.className = `customer-row ${customer.id === state.selectedCustomerId ? "active" : ""}`;

  const selectButton = document.createElement("button");
  selectButton.type = "button";
  selectButton.className = "customer-select";
  selectButton.addEventListener("click", () => selectCustomer(customer.id));

  const dot = document.createElement("span");
  dot.className = "status-dot";
  dot.style.background = markerStyleFor(customer).color;

  const body = document.createElement("span");
  const title = document.createElement("span");
  title.className = "row-title";
  title.textContent = customer.companyName;
  const meta = document.createElement("span");
  meta.className = "row-meta";
  meta.textContent = `${customer.city} ${customer.postcode} | ${typeMeta[customer.type]?.label || customer.type}`;
  body.append(title, meta);

  const editButton = document.createElement("button");
  editButton.type = "button";
  editButton.className = "row-edit-button light-button";
  editButton.textContent = "Edit";
  editButton.addEventListener("click", () => openEditCustomerModal(customer.id));

  selectButton.append(dot, body);
  row.append(selectButton, editButton);
  return row;
}

function filteredCustomersForMap() {
  const selectedTypes = state.filters.types;
  const selectedCompetitors = state.filters.competitors;
  const selectedHappyGroup = selectedHappyGroupIds();

  let rows = [];
  if (selectedTypes.size > 0 || state.filters.date || selectedCompetitors.size > 0) {
    rows = state.customers.filter((customer) => {
      if (selectedTypes.size > 0 && !selectedTypes.has(customer.type)) {
        return false;
      }
      if (state.filters.date && !matchesDateFilter(customer)) {
        return false;
      }
      if (selectedCompetitors.size > 0 && !selectedCompetitors.has(customer.competitor || "")) {
        return false;
      }
      return true;
    });
  }

  if (selectedHappyGroup.size > 0) {
    const existing = new Set(rows.map((customer) => customer.id));
    state.customers.forEach((customer) => {
      if (selectedHappyGroup.has(customer.id) && !existing.has(customer.id)) {
        rows.push(customer);
      }
    });
  }

  return rows;
}

function customersForMapMarkers() {
  const rows = filteredCustomersForMap();
  if (!state.selectedCustomerId || rows.some((customer) => customer.id === state.selectedCustomerId)) {
    return rows;
  }

  const selectedCustomer = state.customers.find((customer) => customer.id === state.selectedCustomerId);
  return selectedCustomer ? [...rows, selectedCustomer] : rows;
}

function selectedHappyGroupIds() {
  const groupId = Number(state.filters.happyGroupId);
  const group = state.happyGroups.find((item) => item.id === groupId);
  return new Set(group?.customerIds || []);
}

function matchesDateFilter(customer) {
  if (customer.type !== "TERMINATION" || !customer.terminationDate) {
    return false;
  }
  if (state.filters.date === "all") {
    return true;
  }

  const days = { "1m": 31, "3m": 93, "6m": 186, "1y": 366 }[state.filters.date] || 0;
  const cutoff = Date.now() - days * 24 * 60 * 60 * 1000;
  return Date.parse(customer.terminationDate) >= cutoff;
}

function renderPins(fit = false) {
  if (state.google.enabled) {
    renderGoogleMarkers(fit);
    return;
  }
  renderInternalPins();
}

function renderInternalPins() {
  const layer = $("#pinLayer");
  layer.replaceChildren();
  customersForMapMarkers().forEach((customer) => {
    const style = markerStyleFor(customer);
    const point = coordsToPoint(customer.latitude, customer.longitude);
    const pin = document.createElement("button");
    pin.type = "button";
    pin.className = `pin ${customer.id === state.selectedCustomerId ? "selected" : ""}`;
    pin.style.left = `${point.x}%`;
    pin.style.top = `${point.y}%`;
    pin.style.background = style.color;
    pin.textContent = style.letter;
    pin.title = customer.companyName;
    pin.addEventListener("click", () => selectCustomer(customer.id));
    pin.addEventListener("pointerdown", (event) => startDrag(event, customer.id));
    layer.append(pin);
  });
  updateMapTransform();
}

function renderGoogleMarkers(fit) {
  const map = state.google.map;
  const AdvancedMarkerElement = state.google.AdvancedMarkerElement;
  if (!map || !AdvancedMarkerElement) {
    return;
  }

  state.google.markers.forEach((marker) => {
    marker.map = null;
  });
  state.google.markers.clear();

  const customers = customersForMapMarkers();
  const googleBounds = new google.maps.LatLngBounds();
  customers.forEach((customer) => {
    const position = { lat: customer.latitude, lng: customer.longitude };
    const marker = new AdvancedMarkerElement({
      map,
      position,
      title: customer.companyName,
      content: createGoogleMarkerContent(customer),
      gmpDraggable: true
    });
    marker.addListener("click", () => selectCustomer(customer.id));
    marker.addListener("dragend", async () => {
      const dropped = readGoogleMarkerPosition(marker.position);
      await updateCustomerCoordinates(customer.id, dropped.lat, dropped.lng);
    });
    state.google.markers.set(customer.id, marker);
    googleBounds.extend(position);
  });

  if (fit && customers.length > 0) {
    map.fitBounds(googleBounds, 48);
  }
}

function createGoogleMarkerContent(customer) {
  const style = markerStyleFor(customer);
  const marker = document.createElement("div");
  marker.className = `google-marker ${customer.id === state.selectedCustomerId ? "selected" : ""}`;
  marker.style.background = style.color;
  marker.textContent = style.letter;
  return marker;
}

function markerStyleFor(customer) {
  if (selectedHappyGroupIds().has(customer.id)) {
    return { color: "#ec4899", letter: "H" };
  }
  if (state.filters.competitors.size > 0 && customer.competitor && competitorMeta[customer.competitor]) {
    return competitorMeta[customer.competitor];
  }
  return typeMeta[customer.type] || typeMeta.PROSPECT;
}

function readGoogleMarkerPosition(position) {
  return {
    lat: typeof position.lat === "function" ? position.lat() : Number(position.lat),
    lng: typeof position.lng === "function" ? position.lng() : Number(position.lng)
  };
}

function selectCustomer(customerId) {
  state.selectedCustomerId = customerId;
  renderFilterCustomerList();
  renderPins(false);
  focusSelectedGoogleMarker();
}

function focusSelectedGoogleMarker() {
  if (!state.google.enabled || !state.google.map) {
    return;
  }
  const customer = state.customers.find((item) => item.id === state.selectedCustomerId);
  const marker = state.google.markers.get(state.selectedCustomerId);
  if (!customer || !marker) {
    return;
  }
  const position = { lat: customer.latitude, lng: customer.longitude };
  state.google.map.panTo(position);
  if ((state.google.map.getZoom() || 4) < 16) {
    state.google.map.setZoom(16);
  }
  state.google.infoWindow?.close();
  state.google.infoWindow?.setContent(`
    <div class="map-info-window">
      <strong>${escapeHtml(customer.companyName)}</strong>
      <span>${escapeHtml(customer.address)}, ${escapeHtml(customer.city)} ${escapeHtml(customer.postcode)}</span>
      <small>${escapeHtml(typeMeta[customer.type]?.label || customer.type)}</small>
      ${customer.generalNote ? `<div class="map-info-note"><b>Note</b>${escapeHtml(customer.generalNote)}</div>` : ""}
    </div>
  `);
  state.google.infoWindow?.open({ map: state.google.map, anchor: marker });
}

function openNewCustomerModal() {
  state.editingCustomerId = null;
  $("#newCustomerForm").reset();
  $("#newCustomerTitle").textContent = "New";
  $("#newCustomerForm button[type='submit']").textContent = "Add Customer";
  $("#newCustomerForm").elements.city.value = "Lidcombe";
  $("#newCustomerForm").elements.state.value = "NSW";
  $("#newCustomerForm").elements.postcode.value = "2141";
  $("#newCustomerForm").elements.calculatedLatitude.value = "";
  $("#newCustomerForm").elements.calculatedLongitude.value = "";
  $("#geocodeStatus").textContent = "Enter Latitude and Longitude to place this store on Google Maps.";
  state.lastAddressPreviewKey = "";
  clearAddressPreviewMarker();
  toggleTerminationFields();
  $("#newCustomerModal").hidden = false;
  $("#newCustomerForm").elements.companyName.focus();
}

function openEditCustomerModal(customerId) {
  const customer = state.customers.find((item) => item.id === customerId);
  if (!customer) {
    setStatus("Customer could not be found.");
    return;
  }

  state.editingCustomerId = customerId;
  const form = $("#newCustomerForm");
  form.reset();
  $("#newCustomerTitle").textContent = "Edit Customer";
  form.querySelector("button[type='submit']").textContent = "Save Changes";
  setFormValue(form, "companyName", customer.companyName);
  setFormValue(form, "type", customer.type);
  setFormValue(form, "terminationDate", customer.terminationDate || "");
  setFormValue(form, "terminationReason", customer.terminationReason || "");
  setFormValue(form, "competitor", customer.competitor || "");
  setFormValue(form, "abn", customer.abn || "");
  setFormValue(form, "address", customer.address);
  setFormValue(form, "city", customer.city);
  setFormValue(form, "state", customer.state);
  setFormValue(form, "postcode", customer.postcode);
  setFormValue(form, "calculatedLatitude", formatCoordinate(customer.latitude));
  setFormValue(form, "calculatedLongitude", formatCoordinate(customer.longitude));
  setFormValue(form, "generalNote", customer.generalNote || "");
  $("#geocodeStatus").textContent = "Update Latitude and Longitude to move this store on Google Maps.";
  state.lastAddressPreviewKey = "";
  showAddressPreviewOnMap({
    latitude: customer.latitude,
    longitude: customer.longitude,
    message: "Existing customer location."
  });
  toggleTerminationFields();
  $("#newCustomerModal").hidden = false;
  form.elements.companyName.focus();
}

function setFormValue(form, name, value) {
  const field = form.elements.namedItem(name);
  if (field) {
    field.value = value ?? "";
  }
}

function closeNewCustomerModal() {
  $("#newCustomerModal").hidden = true;
  clearTimeout(state.addressPreviewTimer);
  clearAddressPreviewMarker();
}

function toggleTerminationFields() {
  const isTermination = $("#newCustomerType").value === "TERMINATION";
  document.querySelectorAll(".termination-field").forEach((field) => {
    field.hidden = !isTermination;
    field.querySelectorAll("input, textarea").forEach((input) => {
      input.required = isTermination;
      if (!isTermination) {
        input.value = "";
      }
    });
  });
}

async function createCustomer(event) {
  event.preventDefault();
  const form = event.currentTarget;
  let data = new FormData(form);
  syncParsedAddressToForm(parseAddressInput(data));
  data = new FormData(form);
  const submitButton = form.querySelector('button[type="submit"]');
  const geocodeStatus = $("#geocodeStatus");
  const files = Array.from(form.elements.photos.files || []);
  const manualCoordinates = readManualCoordinates(data);

  if (files.length > 3) {
    geocodeStatus.textContent = "Choose File supports up to 3 images.";
    return;
  }
  if (String(data.get("type")) === "TERMINATION" && String(data.get("terminationReason") || "").trim().length > 100) {
    geocodeStatus.textContent = "Termination Reason must be 100 characters or fewer.";
    return;
  }
  if (String(data.get("generalNote") || "").trim().length > 200) {
    geocodeStatus.textContent = "Note must be 200 characters or fewer.";
    return;
  }
  if (!manualCoordinates.valid) {
    geocodeStatus.textContent = manualCoordinates.message;
    return;
  }

  const editingCustomer = state.editingCustomerId
    ? state.customers.find((customer) => customer.id === state.editingCustomerId)
    : null;
  const isEditing = Boolean(editingCustomer);

  submitButton.disabled = true;
  geocodeStatus.textContent = "Saving customer details with manual coordinates...";

  try {
    const geocode = {
      latitude: manualCoordinates.latitude,
      longitude: manualCoordinates.longitude,
      message: "Saved with manually entered coordinates."
    };
    updateCalculatedCoordinateFields(geocode);
    showAddressPreviewOnMap(geocode);
    geocodeStatus.textContent = geocode.message;
    const customer = await api(isEditing ? `/api/customers/${editingCustomer.id}` : "/api/customers", {
      method: isEditing ? "PUT" : "POST",
      body: JSON.stringify({
        companyName: data.get("companyName"),
        abn: data.get("abn"),
        address: data.get("address"),
        city: data.get("city"),
        state: data.get("state"),
        postcode: data.get("postcode"),
        phone: "",
        email: "",
        latitude: geocode.latitude,
        longitude: geocode.longitude,
        type: data.get("type"),
        terminationDate: data.get("terminationDate") || null,
        terminationReason: data.get("terminationReason") || null,
        competitor: data.get("competitor") || null,
        generalNote: data.get("generalNote") || null,
        groupId: null,
        assignedUserId: state.user?.role === "ADMIN" ? 2 : null
      })
    });

    await saveInitialPhotos(customer.id, files, data, isEditing);
    closeNewCustomerModal();
    await loadCustomers();
    renderAll();
    selectCustomer(customer.id);
    setStatus(`${isEditing ? "Customer updated" : "Customer added"}. ${geocode.message}`);
    state.editingCustomerId = null;
  } catch (error) {
    const message = error.message || "Customer could not be saved.";
    geocodeStatus.textContent = message;
    setStatus(message);
  } finally {
    submitButton.disabled = false;
  }
}

function syncAddressFieldsFromCurrentForm() {
  const form = $("#newCustomerForm");
  syncParsedAddressToForm(parseAddressInput(new FormData(form)));
}

function scheduleCoordinatePreview() {
  clearTimeout(state.addressPreviewTimer);
  state.addressPreviewTimer = setTimeout(previewManualCoordinates, 350);
}

function previewManualCoordinates() {
  const modal = $("#newCustomerModal");
  if (modal.hidden) {
    return;
  }

  const form = $("#newCustomerForm");
  const data = new FormData(form);
  const coordinates = readManualCoordinates(data, { allowEmpty: true });
  if (coordinates.empty) {
    clearAddressPreviewMarker();
    $("#geocodeStatus").textContent = "Enter Latitude and Longitude to place this store on Google Maps.";
    return;
  }

  if (!coordinates.valid) {
    $("#geocodeStatus").textContent = coordinates.message;
    return;
  }

  const geocode = {
    latitude: coordinates.latitude,
    longitude: coordinates.longitude,
    message: "Manual coordinate preview."
  };
  showAddressPreviewOnMap(geocode);
  $("#geocodeStatus").textContent = `Manual coordinate preview. Latitude ${formatCoordinate(geocode.latitude)}, Longitude ${formatCoordinate(geocode.longitude)}.`;
}

function readManualCoordinates(data, options = {}) {
  const latitudeText = String(data.get ? data.get("calculatedLatitude") : data.calculatedLatitude || "").trim();
  const longitudeText = String(data.get ? data.get("calculatedLongitude") : data.calculatedLongitude || "").trim();
  const empty = !latitudeText && !longitudeText;
  if (empty && options.allowEmpty) {
    return { empty: true, valid: false };
  }
  if (!latitudeText || !longitudeText) {
    return { valid: false, message: "Latitude and Longitude are required." };
  }

  const latitude = Number(latitudeText);
  const longitude = Number(longitudeText);
  if (!Number.isFinite(latitude) || latitude < -90 || latitude > 90) {
    return { valid: false, message: "Latitude must be a number between -90 and 90." };
  }
  if (!Number.isFinite(longitude) || longitude < -180 || longitude > 180) {
    return { valid: false, message: "Longitude must be a number between -180 and 180." };
  }

  return { valid: true, latitude, longitude };
}

function syncParsedAddressToForm(parsed) {
  const form = $("#newCustomerForm");
  if (parsed.address && normalizeAddressKey(parsed.address) !== normalizeAddressKey(parsed.rawAddress)) {
    setFormValue(form, "address", parsed.address);
  }
  if (parsed.city) {
    setFormValue(form, "city", parsed.city);
  }
  if (parsed.state) {
    setFormValue(form, "state", parsed.state);
  }
  if (parsed.postcode) {
    setFormValue(form, "postcode", parsed.postcode);
  }
}

function updateCalculatedCoordinateFields(geocode) {
  const form = $("#newCustomerForm");
  form.elements.calculatedLatitude.value = geocode ? formatCoordinate(geocode.latitude) : "";
  form.elements.calculatedLongitude.value = geocode ? formatCoordinate(geocode.longitude) : "";
}

function showAddressPreviewOnMap(geocode) {
  if (!geocode || !state.google.enabled || !state.google.map || !state.google.AdvancedMarkerElement) {
    return;
  }

  const position = { lat: geocode.latitude, lng: geocode.longitude };
  clearAddressPreviewMarker();
  const marker = document.createElement("div");
  marker.className = "google-marker preview-marker";
  marker.textContent = "P";
  state.google.previewMarker = new state.google.AdvancedMarkerElement({
    map: state.google.map,
    position,
    title: "Address preview",
    content: marker
  });
  state.google.map.panTo(position);
  state.google.map.setZoom(16);
}

function clearAddressPreviewMarker() {
  if (state.google.previewMarker) {
    state.google.previewMarker.map = null;
    state.google.previewMarker = null;
  }
}

async function saveInitialPhotos(customerId, files, data, isEditing = false) {
  const noteText = String(data.get("generalNote") || "").trim() || "Initial customer photo";
  if (files.length === 0 && (isEditing || !String(data.get("generalNote") || "").trim())) {
    return;
  }

  if (files.length === 0) {
    const noteData = new FormData();
    noteData.set("competitorProduct", data.get("competitor") || "");
    noteData.set("notes", noteText);
    await api(`/api/customers/${customerId}/notes`, { method: "POST", body: noteData });
    return;
  }

  for (const file of files.slice(0, 3)) {
    const noteData = new FormData();
    noteData.set("competitorProduct", data.get("competitor") || "");
    noteData.set("notes", noteText);
    noteData.set("image", file);
    await api(`/api/customers/${customerId}/notes`, { method: "POST", body: noteData });
  }
}

function parseAddressInput(data) {
  const rawAddress = String(data.get ? data.get("address") : data.address || "").trim();
  let city = String(data.get ? data.get("city") : data.city || "").trim();
  let postcode = String(data.get ? data.get("postcode") : data.postcode || "").trim();
  let stateName = String(data.get ? data.get("state") : data.state || "NSW").trim().toUpperCase();
  let address = rawAddress;
  const compactAddress = rawAddress.replace(/\s+/g, " ").trim();

  const commaParts = rawAddress.split(",").map((part) => part.trim()).filter(Boolean);
  if (commaParts.length > 1) {
    address = commaParts[0];
    const locality = commaParts.slice(1).join(" ");
    const parsedLocality = locality.match(/\b([A-Za-z][A-Za-z\s'-]+?)\s+(NSW|VIC|QLD|SA|WA|ACT|TAS|NT)?\s*(\d{4})\b/i);
    if (parsedLocality) {
      city = parsedLocality[1].trim();
      stateName = (parsedLocality[2] || stateName || "NSW").toUpperCase();
      postcode = parsedLocality[3];
    }
  } else {
    const parsedTail = compactAddress.match(/\b(NSW|VIC|QLD|SA|WA|ACT|TAS|NT)?\s*(\d{4})\s*$/i);
    if (parsedTail) {
      postcode = parsedTail[2];
      stateName = (parsedTail[1] || stateName || "NSW").toUpperCase();
      const beforePostcode = compactAddress.slice(0, parsedTail.index).trim().replace(/[,\s]+$/, "");
      const streetAndSuburb = splitStreetAndSuburb(beforePostcode);
      if (streetAndSuburb) {
        address = streetAndSuburb.address;
        city = streetAndSuburb.city;
      }
    }
  }

  return { address, city, state: stateName || "NSW", postcode, rawAddress };
}

function splitStreetAndSuburb(value) {
  const roadSuffix = /\b(st|street|rd|road|ave|avenue|hwy|highway|dr|drive|ln|lane|ct|court|pl|place|pde|parade|cres|crescent|way)\b\.?/ig;
  let match;
  let lastSuffixEnd = -1;
  while ((match = roadSuffix.exec(value)) !== null) {
    lastSuffixEnd = roadSuffix.lastIndex;
  }

  if (lastSuffixEnd < 0) {
    return null;
  }

  const address = value.slice(0, lastSuffixEnd).trim().replace(/[,\s]+$/, "");
  const city = value.slice(lastSuffixEnd).trim().replace(/^[,\s]+/, "");
  return address && city ? { address, city } : null;
}

function normalizeAddressKey(value) {
  return String(value || "")
    .toLowerCase()
    .replace(/\bstreet\b/g, "st")
    .replace(/\bnew south wales\b/g, "nsw")
    .replace(/[^a-z0-9]+/g, " ")
    .replace(/\s+/g, " ")
    .trim();
}

function clearMapFilters() {
  state.filters.types.clear();
  state.filters.date = "";
  state.filters.competitors.clear();
  state.filters.happyGroupId = "";
  document.querySelectorAll("#mapTypeFilters input, #competitorFilters input, #dateFilters input").forEach((input) => {
    input.checked = false;
  });
  $("#happyVisitFilter").value = "";
  renderAll();
  resetGoogleMapToSydney();
}

function onHappyVisitFilterChange(event) {
  state.filters.happyGroupId = event.target.value;
  renderAll();
  resetGoogleMapToSydney();
}

function renderHappyVisitSelect() {
  const select = $("#happyVisitFilter");
  const current = select.value;
  select.replaceChildren(new Option("Select group", ""));
  state.happyGroups.forEach((group) => {
    select.add(new Option(group.groupName, group.id));
  });
  select.value = state.happyGroups.some((group) => String(group.id) === current) ? current : "";
  state.filters.happyGroupId = select.value;
}

function onHappyTypeChange(event) {
  if (state.happy.selectedIds.size > 0) {
    const ok = confirm("Selected customers will stay in Group List. Change Type?");
    if (!ok) {
      event.target.value = state.happy.type;
      return;
    }
  }
  state.happy.type = event.target.value;
  state.happy.page = 1;
  renderHappyCustomerList();
}

function renderHappyVisit() {
  renderHappyCustomerList();
  renderHappySelectedList();
  renderHappySavedGroups();
}

function happySourceCustomers() {
  if (!state.happy.type) {
    return [];
  }
  return state.customers
    .filter((customer) => customer.type === state.happy.type && !state.happy.selectedIds.has(customer.id))
    .sort((a, b) => a.companyName.localeCompare(b.companyName));
}

function renderHappyCustomerList() {
  const list = $("#happyCustomerList");
  list.replaceChildren();
  const rows = happySourceCustomers();
  const totalPages = Math.max(1, Math.ceil(rows.length / state.happy.pageSize));
  state.happy.page = Math.min(state.happy.page, totalPages);
  const start = (state.happy.page - 1) * state.happy.pageSize;
  rows.slice(start, start + state.happy.pageSize).forEach((customer) => {
    const label = document.createElement("label");
    label.className = "select-row";
    const input = document.createElement("input");
    input.type = "checkbox";
    input.value = customer.id;
    label.append(input, document.createTextNode(`${customer.companyName} - ${customer.city} ${customer.postcode}`));
    list.append(label);
  });
  $("#happyPageLabel").textContent = rows.length === 0 ? "0 / 0" : `${state.happy.page} / ${totalPages}`;
}

function renderHappySelectedList() {
  const list = $("#happySelectedList");
  list.replaceChildren();
  selectedHappyCustomers().forEach((customer) => {
    const row = document.createElement("button");
    row.type = "button";
    row.className = "select-row remove-row";
    row.textContent = `${customer.companyName} - ${customer.city} ${customer.postcode}`;
    row.addEventListener("click", () => {
      state.happy.selectedIds.delete(customer.id);
      renderHappyVisit();
    });
    list.append(row);
  });
}

function renderHappySavedGroups() {
  const list = $("#happyGroupList");
  list.replaceChildren();
  state.happyGroups.forEach((group) => {
    const button = document.createElement("button");
    button.type = "button";
    button.className = "saved-group-row";
    button.textContent = `${group.groupName} | ${titleCase(group.type)} | ${group.customerIds.length} customers`;
    button.addEventListener("click", () => {
      $("#happyGroupName").value = group.groupName;
      $("#happyTypeSelect").value = group.type;
      state.happy.type = group.type;
      state.happy.selectedIds = new Set(group.customerIds);
      $("#happySaveButton").dataset.groupId = group.id;
      renderHappyVisit();
    });
    list.append(button);
  });
}

function selectedHappyCustomers() {
  return state.customers.filter((customer) => state.happy.selectedIds.has(customer.id));
}

function addHappySelectedCustomers() {
  document.querySelectorAll("#happyCustomerList input:checked").forEach((input) => {
    state.happy.selectedIds.add(Number(input.value));
  });
  renderHappyVisit();
}

function changeHappyPage(delta) {
  state.happy.page = Math.max(1, state.happy.page + delta);
  renderHappyCustomerList();
}

function resetHappyEditor() {
  $("#happyGroupName").value = "";
  $("#happyTypeSelect").value = "";
  $("#happySaveButton").dataset.groupId = "";
  state.happy.type = "";
  state.happy.selectedIds.clear();
  state.happy.page = 1;
  $("#happyVisitStatus").textContent = "";
  renderHappyVisit();
}

async function saveHappyGroup() {
  const groupName = $("#happyGroupName").value.trim();
  const type = $("#happyTypeSelect").value;
  if (!groupName) {
    $("#happyVisitStatus").textContent = "Group Name is required.";
    return;
  }
  if (!type) {
    $("#happyVisitStatus").textContent = "Type is required.";
    return;
  }
  if (state.happy.selectedIds.size === 0) {
    $("#happyVisitStatus").textContent = "Please add at least one customer.";
    return;
  }

  const groupId = Number($("#happySaveButton").dataset.groupId || 0);
  try {
    await api(groupId ? `/api/happy-visits/${groupId}` : "/api/happy-visits", {
      method: groupId ? "PUT" : "POST",
      body: JSON.stringify({
        groupName,
        type,
        customerIds: Array.from(state.happy.selectedIds)
      })
    });
    await loadHappyGroups();
    resetHappyEditor();
    renderAll();
    setStatus("Happy Visit group saved");
  } catch (error) {
    $("#happyVisitStatus").textContent = error.message;
  }
}

function showPostForm() {
  state.editingPostId = null;
  $("#postBoard").hidden = true;
  $("#postDetail").hidden = true;
  $("#postForm").hidden = false;
  $("#postFormTitle").textContent = "Post";
  $("#postSubmitButton").textContent = "Post";
  $("#postForm").reset();
}

function backFromPostForm() {
  if (confirm("Are you sure you want to leave?")) {
    $("#postForm").hidden = true;
    $("#postDetail").hidden = true;
    $("#postBoard").hidden = false;
    state.editingPostId = null;
  }
}

async function savePost(event) {
  event.preventDefault();
  const form = event.currentTarget;
  const files = Array.from(form.elements.images.files || []);
  if (files.length > 3) {
    setStatus("Upload supports up to 3 images.");
    return;
  }
  const formData = new FormData(form);
  formData.delete("images");
  files.forEach((file) => formData.append("images", file));
  try {
    await api(state.editingPostId ? `/api/dashboard/posts/${state.editingPostId}` : "/api/dashboard/posts", {
      method: state.editingPostId ? "PUT" : "POST",
      body: formData
    });
    await loadPosts();
    renderPosts();
    form.hidden = true;
    $("#postDetail").hidden = true;
    $("#postBoard").hidden = false;
    setStatus(state.editingPostId ? "Post updated" : "Post saved");
    state.editingPostId = null;
  } catch (error) {
    setStatus(error.message);
  }
}

function renderPosts() {
  const list = $("#postList");
  list.replaceChildren();
  if (state.posts.length === 0) {
    const empty = document.createElement("div");
    empty.className = "empty-state";
    empty.textContent = "No posts yet.";
    list.append(empty);
    return;
  }

  state.posts.forEach((post) => {
    const card = document.createElement("article");
    card.className = "post-card";
    const date = new Date(post.createdAt).toLocaleDateString();
    card.innerHTML = `
      <strong>${escapeHtml(post.postName)}</strong>
      <small>${escapeHtml(post.editor)} | ${date}</small>
      <p>${escapeHtml(post.description)}</p>
    `;
    const actions = document.createElement("div");
    actions.className = "post-actions";
    const openButton = document.createElement("button");
    openButton.type = "button";
    openButton.className = "light-button";
    openButton.textContent = "Open";
    openButton.addEventListener("click", () => openPostDetail(post.id));
    const editButton = document.createElement("button");
    editButton.type = "button";
    editButton.textContent = "Edit";
    editButton.addEventListener("click", () => editPost(post.id));
    const deleteButton = document.createElement("button");
    deleteButton.type = "button";
    deleteButton.className = "danger-button";
    deleteButton.textContent = "Delete";
    deleteButton.addEventListener("click", () => deletePost(post.id));
    actions.append(openButton, editButton, deleteButton);
    if (post.imagePaths?.length) {
      const images = document.createElement("div");
      images.className = "post-images";
      post.imagePaths.slice(0, 3).forEach((path) => {
        const img = document.createElement("img");
        img.src = path;
        img.alt = post.postName;
        images.append(img);
      });
      card.append(images);
    }
    card.append(actions);
    list.append(card);
  });
}

function openPostDetail(postId) {
  const post = state.posts.find((item) => item.id === postId);
  if (!post) {
    setStatus("Post could not be found.");
    return;
  }

  $("#postBoard").hidden = true;
  $("#postForm").hidden = true;
  const detail = $("#postDetail");
  detail.hidden = false;
  const date = new Date(post.createdAt).toLocaleString();
  detail.innerHTML = `
    <div class="section-head">
      <h2>${escapeHtml(post.postName)}</h2>
      <button id="closePostDetailButton" type="button" class="light-button">Back</button>
    </div>
    <small>${escapeHtml(post.editor)} | ${date}</small>
    <p>${escapeHtml(post.description)}</p>
    <div class="post-images">${(post.imagePaths || []).map((path) =>
      `<img src="${escapeHtml(path)}" alt="${escapeHtml(post.postName)}">`).join("")}</div>
    <div class="post-actions">
      <button id="detailEditPostButton" type="button">Edit</button>
      <button id="detailDeletePostButton" type="button" class="danger-button">Delete</button>
    </div>
  `;
  $("#closePostDetailButton").addEventListener("click", () => {
    detail.hidden = true;
    $("#postBoard").hidden = false;
  });
  $("#detailEditPostButton").addEventListener("click", () => editPost(postId));
  $("#detailDeletePostButton").addEventListener("click", () => deletePost(postId));
}

function editPost(postId) {
  const post = state.posts.find((item) => item.id === postId);
  if (!post) {
    setStatus("Post could not be found.");
    return;
  }

  state.editingPostId = postId;
  const form = $("#postForm");
  form.reset();
  form.elements.postName.value = post.postName;
  form.elements.editor.value = post.editor;
  form.elements.description.value = post.description;
  $("#postFormTitle").textContent = "Edit Post";
  $("#postSubmitButton").textContent = "Save";
  $("#postBoard").hidden = true;
  $("#postDetail").hidden = true;
  form.hidden = false;
}

async function deletePost(postId) {
  const post = state.posts.find((item) => item.id === postId);
  if (!post) {
    setStatus("Post could not be found.");
    return;
  }

  if (!confirm(`Delete "${post.postName}"?`)) {
    return;
  }

  try {
    await api(`/api/dashboard/posts/${postId}`, { method: "DELETE" });
    await loadPosts();
    renderPosts();
    $("#postForm").hidden = true;
    $("#postDetail").hidden = true;
    $("#postBoard").hidden = false;
    setStatus("Post deleted");
  } catch (error) {
    setStatus(error.message);
  }
}

function exportCustomerXmlTemplate() {
  const xml = buildCustomerTemplateXml();
  downloadTextFile("ecnesoft-customer-import-template.xml", xml, "application/xml");
  setStatus("Customer XML template exported. Fill row 2 onward and import it from Customer.");
}

function buildCustomerTemplateXml() {
  const customerRows = state.customers.map(customerXmlRow);
  const blankRowCount = Math.max(10, 35 - customerRows.length);
  const rows = [
    customerXmlTemplateFields.map((field) => field.label),
    ...customerRows,
    ...Array.from({ length: blankRowCount }, () => customerXmlTemplateFields.map(() => ""))
  ];

  return `<?xml version="1.0" encoding="UTF-8"?>
<?mso-application progid="Excel.Sheet"?>
<Workbook xmlns="urn:schemas-microsoft-com:office:spreadsheet"
  xmlns:o="urn:schemas-microsoft-com:office:office"
  xmlns:x="urn:schemas-microsoft-com:office:excel"
  xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
  <DocumentProperties xmlns="urn:schemas-microsoft-com:office:office">
    <Author>ECNESOFT Field Sales</Author>
    <Created>${new Date().toISOString()}</Created>
  </DocumentProperties>
  <Worksheet ss:Name="Customer Import">
    <Table>
${customerXmlTemplateFields.map((field) => `      <Column ss:AutoFitWidth="1" ss:Width="${Math.max(70, field.label.length * 7)}"/>`).join("\n")}
${rows.map((row) => `      <Row>${row.map((cell) => `<Cell><Data ss:Type="String">${escapeXml(cell)}</Data></Cell>`).join("")}</Row>`).join("\n")}
    </Table>
  </Worksheet>
</Workbook>
`;
}

function customerXmlRow(customer) {
  const values = {
    companyName: customer.companyName,
    type: customer.type,
    terminationDate: customer.terminationDate || "",
    terminationReason: customer.terminationReason || "",
    competitor: customer.competitor || "",
    abn: customer.abn || "",
    address: customer.address,
    city: customer.city,
    state: customer.state,
    postcode: customer.postcode,
    latitude: formatCoordinate(customer.latitude),
    longitude: formatCoordinate(customer.longitude),
    generalNote: customer.generalNote || ""
  };

  return customerXmlTemplateFields.map((field) => values[field.key] ?? "");
}

function downloadTextFile(fileName, contents, mimeType) {
  const blob = new Blob([contents], { type: `${mimeType};charset=utf-8` });
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = fileName;
  document.body.append(link);
  link.click();
  link.remove();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}

async function importCustomerXml(event) {
  const input = event.currentTarget;
  const file = input.files?.[0];
  if (!file) {
    return;
  }

  setStatus("Importing customer XML...");
  try {
    const records = parseCustomerImportXml(await file.text());
    const payloads = [];
    const validationErrors = [];
    records.forEach((record, index) => {
      try {
        payloads.push(buildCustomerImportPayload(record, index + 2));
      } catch (error) {
        validationErrors.push(`Row ${index + 2}: ${error.message}`);
      }
    });

    if (payloads.length === 0) {
      throw new Error("No valid customer rows were found in the XML file.");
    }
    if (validationErrors.length > 0) {
      throw new Error(`Import cancelled. ${validationErrors.slice(0, 3).join(" ")}`);
    }

    let inserted = 0;
    const errors = [];
    const importedTypes = new Set();

    const clearResult = await api("/api/customers", { method: "DELETE" });
    for (let index = 0; index < payloads.length; index += 1) {
      try {
        const payload = payloads[index];
        await api("/api/customers", {
          method: "POST",
          body: JSON.stringify(payload)
        });
        inserted += 1;
        importedTypes.add(payload.type);
      } catch (error) {
        errors.push(`Row ${index + 1}: ${error.message}`);
      }
    }

    await loadCustomers();
    importedTypes.forEach((type) => state.customerFilters.types.add(type));
    syncCustomerTypeFilterInputs();
    renderAll();
    renderCustomerTable();
    showView("customerView");
    const summary = `${inserted} customer${inserted === 1 ? "" : "s"} imported`;
    setStatus(errors.length
      ? `${summary}. ${clearResult.deleted || 0} old customers deleted. ${errors.slice(0, 3).join(" ")}`
      : `${summary}. ${clearResult.deleted || 0} old customers deleted.`);
  } catch (error) {
    setStatus(error.message || "XML import failed.");
  } finally {
    input.value = "";
  }
}

function syncCustomerTypeFilterInputs() {
  document.querySelectorAll("#customerTypeFilters input").forEach((input) => {
    input.checked = state.customerFilters.types.has(input.value);
  });
}

function parseCustomerImportXml(xmlText) {
  const doc = new DOMParser().parseFromString(xmlText, "application/xml");
  if (elementsByLocalName(doc, "parsererror").length > 0) {
    throw new Error("XML file could not be read. Please use the exported template.");
  }

  const spreadsheetRecords = parseSpreadsheetImportRecords(doc);
  if (spreadsheetRecords.length > 0) {
    return spreadsheetRecords;
  }

  const elementRecords = parseCustomerElementRecords(doc);
  if (elementRecords.length > 0) {
    return elementRecords;
  }

  throw new Error("No customer data was found in the XML file.");
}

function parseSpreadsheetImportRecords(doc) {
  const worksheets = elementsByLocalName(doc, "Worksheet");
  const scopes = worksheets.length > 0 ? worksheets : [doc.documentElement];
  const records = [];

  scopes.forEach((scope) => {
    const rows = elementsByLocalName(scope, "Row").map(spreadsheetRowValues);
    const hasHorizontalHeader = rows.some((row) => row.filter((cell) => importKeyFor(cell)).length > 1);
    const horizontalRecords = parseHorizontalRows(rows);
    if (horizontalRecords.length > 0 || hasHorizontalHeader) {
      horizontalRecords.forEach((record) => {
        if (!isEmptyImportRecord(record)) {
          records.push(record);
        }
      });
      return;
    }

    const pairRecord = parsePairRows(rows);
    if (!isEmptyImportRecord(pairRecord)) {
      records.push(pairRecord);
    }
  });

  return records;
}

function parsePairRows(rows) {
  const record = {};
  rows.forEach((row) => {
    const key = importKeyFor(row[0]);
    if (key) {
      record[key] = String(row[1] || "").trim();
    }
  });
  return record;
}

function parseHorizontalRows(rows) {
  const records = [];
  const headerIndex = rows.findIndex((row) => row.some((cell) => importKeyFor(cell)));
  if (headerIndex < 0) {
    return records;
  }

  const headers = rows[headerIndex].map(importKeyFor);
  rows.slice(headerIndex + 1).forEach((row) => {
    const record = {};
    headers.forEach((key, index) => {
      if (key) {
        record[key] = String(row[index] || "").trim();
      }
    });
    records.push(record);
  });
  return records;
}

function parseCustomerElementRecords(doc) {
  const root = doc.documentElement;
  const customerElements = root.localName.toLowerCase() === "customer"
    ? [root]
    : elementsByLocalName(doc, "Customer");

  return customerElements.map((customer) => {
    const record = {};
    Array.from(customer.children).forEach((child) => {
      if (child.localName.toLowerCase() === "field") {
        const key = importKeyFor(child.getAttribute("name") || child.getAttribute("key") || "");
        if (key) {
          record[key] = child.textContent.trim();
        }
        return;
      }

      const key = importKeyFor(child.localName);
      if (key) {
        record[key] = child.textContent.trim();
      }
    });
    return record;
  }).filter((record) => !isEmptyImportRecord(record));
}

function spreadsheetRowValues(row) {
  const values = [];
  let index = 0;
  elementsByLocalName(row, "Cell").forEach((cell) => {
    const explicitIndex = attributeByLocalName(cell, "Index");
    if (explicitIndex) {
      index = Math.max(0, Number(explicitIndex) - 1);
    }
    const data = elementsByLocalName(cell, "Data")[0];
    values[index] = (data?.textContent || "").trim();
    index += 1;
  });
  return values;
}

function buildCustomerImportPayload(record, rowNumber) {
  const type = normalizeLifecycleType(record.type);
  const competitor = normalizeCompetitorType(record.competitor);
  const latitude = parseImportCoordinate(record.latitude, "Latitude", rowNumber, -90, 90);
  const longitude = parseImportCoordinate(record.longitude, "Longitude", rowNumber, -180, 180);
  const terminationDate = stringOrNull(record.terminationDate);
  const terminationReason = stringOrNull(record.terminationReason);
  const generalNote = stringOrNull(record.generalNote);

  if (!type) {
    throw new Error("Type is required. Use ACTIVE, TERMINATION, CLOSED, PROSPECT, or OWNERSHIP.");
  }
  if (!requiredImportValue(record.companyName)) {
    throw new Error("Company is required.");
  }
  if (!requiredImportValue(record.address) || !requiredImportValue(record.city) ||
      !requiredImportValue(record.state) || !requiredImportValue(record.postcode)) {
    throw new Error("Address, Suburb, State, and Postcode are required.");
  }
  if (type === "TERMINATION" && (!terminationDate || !terminationReason)) {
    throw new Error("Termination Date and Termination Reason are required for TERMINATION.");
  }
  if (terminationReason && terminationReason.length > 100) {
    throw new Error("Termination Reason must be 100 characters or fewer.");
  }
  if (generalNote && generalNote.length > 200) {
    throw new Error("Note must be 200 characters or fewer.");
  }

  return {
    companyName: record.companyName.trim(),
    abn: stringOrNull(record.abn),
    address: record.address.trim(),
    city: record.city.trim(),
    state: record.state.trim().toUpperCase(),
    postcode: record.postcode.trim(),
    phone: "",
    email: "",
    latitude,
    longitude,
    type,
    terminationDate: type === "TERMINATION" ? terminationDate : null,
    terminationReason: type === "TERMINATION" ? terminationReason : null,
    competitor,
    generalNote,
    groupId: null,
    assignedUserId: state.user?.role === "ADMIN" ? 2 : null
  };
}

function normalizeLifecycleType(value) {
  const normalized = normalizeImportKey(value);
  return {
    active: "ACTIVE",
    termination: "TERMINATION",
    terminated: "TERMINATION",
    closed: "CLOSED",
    prospect: "PROSPECT",
    ownership: "OWNERSHIP"
  }[normalized] || "";
}

function normalizeCompetitorType(value) {
  const normalized = normalizeImportKey(value);
  if (!normalized) {
    return null;
  }
  return {
    kpos: "KPOS",
    ordernow: "ORDERNOW",
    qonus: "QONUS",
    square: "SQUARE",
    etc: "ETC"
  }[normalized] || null;
}

function parseImportCoordinate(value, label, rowNumber, min, max) {
  const number = Number(String(value || "").trim());
  if (!Number.isFinite(number) || number < min || number > max) {
    throw new Error(`${label} is invalid on row ${rowNumber}.`);
  }
  return number;
}

function importKeyFor(label) {
  return customerImportAliases.get(normalizeImportKey(label)) || "";
}

function normalizeImportKey(value) {
  return String(value || "").toLowerCase().replace(/[^a-z0-9]+/g, "");
}

function requiredImportValue(value) {
  return String(value || "").trim().length > 0;
}

function stringOrNull(value) {
  const text = String(value || "").trim();
  return text || null;
}

function isEmptyImportRecord(record) {
  return customerXmlTemplateFields
    .filter((field) => !field.key.startsWith("photo"))
    .every((field) => !requiredImportValue(record[field.key]));
}

function elementsByLocalName(root, localName) {
  const elements = Array.from(root.getElementsByTagName("*"));
  if (root.nodeType === 1) {
    elements.unshift(root);
  }
  return elements.filter((element) => element.localName.toLowerCase() === localName.toLowerCase());
}

function attributeByLocalName(element, localName) {
  return Array.from(element.attributes || [])
    .find((attribute) => attribute.localName.toLowerCase() === localName.toLowerCase())?.value || "";
}

function escapeXml(value) {
  return String(value ?? "")
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&apos;");
}

async function updateCustomerCoordinates(customerId, latitude, longitude) {
  try {
    await api(`/api/customers/${customerId}/coordinates`, {
      method: "PATCH",
      body: JSON.stringify({ latitude, longitude })
    });
    const customer = state.customers.find((item) => item.id === customerId);
    if (customer) {
      customer.latitude = latitude;
      customer.longitude = longitude;
    }
    setStatus("Coordinates updated");
  } catch (error) {
    setStatus(error.message);
  }
}

function startDrag(event, customerId) {
  event.preventDefault();
  event.stopPropagation();
  const pin = event.currentTarget;
  pin.setPointerCapture(event.pointerId);
  state.dragging = { customerId, pin };
  selectCustomer(customerId);
}

function startPan(event) {
  if (state.google.enabled || event.target.closest(".pin")) {
    return;
  }
  const canvas = $("#mapCanvas");
  canvas.setPointerCapture(event.pointerId);
  state.map.panning = {
    pointerId: event.pointerId,
    startX: event.clientX,
    startY: event.clientY,
    centerX: state.map.centerX,
    centerY: state.map.centerY
  };
}

function onPointerMove(event) {
  if (state.dragging) {
    const point = screenToWorldPoint(event.clientX, event.clientY);
    state.dragging.pin.style.left = `${point.x}%`;
    state.dragging.pin.style.top = `${point.y}%`;
    return;
  }
  if (state.map.panning) {
    const rect = $("#mapCanvas").getBoundingClientRect();
    const dx = event.clientX - state.map.panning.startX;
    const dy = event.clientY - state.map.panning.startY;
    state.map.centerX = state.map.panning.centerX - (dx / (rect.width * state.map.zoom)) * 100;
    state.map.centerY = state.map.panning.centerY - (dy / (rect.height * state.map.zoom)) * 100;
    clampMapCenter();
    updateMapTransform();
  }
}

async function onPointerUp(event) {
  if (state.map.panning) {
    try {
      $("#mapCanvas").releasePointerCapture(state.map.panning.pointerId);
    } catch {
      // Pointer capture can already be released.
    }
    state.map.panning = null;
  }
  if (!state.dragging) {
    return;
  }
  const { customerId, pin } = state.dragging;
  state.dragging = null;
  try {
    pin.releasePointerCapture(event.pointerId);
  } catch {
    // Pointer capture can already be released.
  }
  const coords = pointToCoords(Number.parseFloat(pin.style.left), Number.parseFloat(pin.style.top));
  await updateCustomerCoordinates(customerId, coords.latitude, coords.longitude);
}

function zoomIn() {
  if (state.google.enabled && state.google.map) {
    state.google.map.setZoom((state.google.map.getZoom() || 4) + 1);
    updateGoogleZoomLabel();
    return;
  }
  setZoom(state.map.zoom * 1.35);
}

function zoomOut() {
  if (state.google.enabled && state.google.map) {
    state.google.map.setZoom((state.google.map.getZoom() || 4) - 1);
    updateGoogleZoomLabel();
    return;
  }
  setZoom(state.map.zoom / 1.35);
}

function resetMap() {
  if (state.google.enabled && state.google.map) {
    renderGoogleMarkers(false);
    resetGoogleMapToSydney();
    return;
  }
  state.map.zoom = 1;
  state.map.centerX = 50;
  state.map.centerY = 50;
  updateMapTransform();
}

function resetGoogleMapToSydney() {
  if (!state.google.enabled || !state.google.map) {
    return;
  }
  state.google.map.setCenter(defaultGoogleView.center);
  state.google.map.setZoom(defaultGoogleView.zoom);
  state.google.infoWindow?.close();
  updateGoogleZoomLabel();
}

function setZoom(nextZoom) {
  state.map.zoom = clamp(nextZoom, 1, 6);
  clampMapCenter();
  updateMapTransform();
}

function onMapWheel(event) {
  if (state.google.enabled) {
    return;
  }
  event.preventDefault();
  setZoom(state.map.zoom * (event.deltaY < 0 ? 1.12 : 0.88));
}

function updateGoogleZoomLabel() {
  if (!state.google.enabled || !state.google.map) {
    return;
  }
  $("#zoomLevel").textContent = `z${state.google.map.getZoom() || 4}`;
}

function coordsToPoint(latitude, longitude) {
  const x = ((longitude - bounds.west) / (bounds.east - bounds.west)) * 100;
  const y = ((bounds.north - latitude) / (bounds.north - bounds.south)) * 100;
  return { x: clamp(x, 2, 98), y: clamp(y, 4, 98) };
}

function pointToCoords(x, y) {
  const latitude = bounds.north - (y / 100) * (bounds.north - bounds.south);
  const longitude = bounds.west + (x / 100) * (bounds.east - bounds.west);
  return {
    latitude: Number(latitude.toFixed(6)),
    longitude: Number(longitude.toFixed(6))
  };
}

function screenToWorldPoint(clientX, clientY) {
  const rect = $("#mapCanvas").getBoundingClientRect();
  const x = ((clientX - rect.left - state.map.tx) / (rect.width * state.map.zoom)) * 100;
  const y = ((clientY - rect.top - state.map.ty) / (rect.height * state.map.zoom)) * 100;
  return { x: clamp(x, 0, 100), y: clamp(y, 0, 100) };
}

function positionMapLabels() {
  document.querySelectorAll(".map-label[data-lat][data-lng]").forEach((label) => {
    const point = coordsToPoint(Number(label.dataset.lat), Number(label.dataset.lng));
    label.style.left = `${point.x}%`;
    label.style.top = `${point.y}%`;
  });
}

function clampMapCenter() {
  const halfVisible = 50 / state.map.zoom;
  state.map.centerX = clamp(state.map.centerX, halfVisible, 100 - halfVisible);
  state.map.centerY = clamp(state.map.centerY, halfVisible, 100 - halfVisible);
}

function updateMapTransform() {
  const canvas = $("#mapCanvas");
  const world = $("#mapWorld");
  if (!canvas || !world || canvas.hidden) {
    return;
  }
  const rect = canvas.getBoundingClientRect();
  state.map.tx = rect.width * 0.5 - (state.map.centerX / 100) * rect.width * state.map.zoom;
  state.map.ty = rect.height * 0.5 - (state.map.centerY / 100) * rect.height * state.map.zoom;
  world.style.transform = `translate(${state.map.tx}px, ${state.map.ty}px) scale(${state.map.zoom})`;
  world.style.setProperty("--map-counter-scale", `${1 / state.map.zoom}`);
  $("#zoomLevel").textContent = `${Math.round(state.map.zoom * 100)}%`;
}

function updateSet(set, value, checked) {
  if (checked) {
    set.add(value);
  } else {
    set.delete(value);
  }
}

function titleCase(value) {
  return String(value || "")
    .toLowerCase()
    .replace(/(^|_)([a-z])/g, (_, __, letter) => letter.toUpperCase());
}

function competitorLabel(value) {
  return competitorMeta[value]?.label || value || "";
}

function formatCoordinate(value) {
  return Number(value).toFixed(7).replace(/0+$/, "").replace(/\.$/, "");
}

function escapeHtml(value) {
  return String(value ?? "")
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#039;");
}

function clamp(value, min, max) {
  return Math.min(max, Math.max(min, value));
}

function setStatus(message) {
  $("#statusMessage").textContent = message;
}
