// AUTO-GENERATED at build time from the C# records in EggIncognito.Capture.
// Do NOT edit by hand; it is regenerated on every build. Field names are camelCased
// to match the JsonSerializerDefaults.Web casing used on the wire.

export interface DashboardFlow {
  id: number;
  timestamp?: string;
  path?: string;
  method?: string;
  status: number;
  requestJson?: string;
  responseJson?: string;
  responseB64?: string;
  requestDataB64?: string;
  requestType?: string;
  responseType?: string;
  known: boolean;
  outcome?: string;
  diffAdded: number;
  diffRemoved: number;
  requestJsonRaw?: string;
  responseJsonRaw?: string;
  url?: string;
  requestHeaders?: DashboardHeader[];
  responseHeaders?: DashboardHeader[];
  requestHeadersRaw?: DashboardHeader[];
  responseHeadersRaw?: DashboardHeader[];
  responseIsAck: boolean;
  responseText?: string;
  saved: boolean;
  observed?: ObservedVersion;
}

export interface DashboardHeader {
  name?: string;
  value?: string;
  sensitive: boolean;
}

export interface CaptureStats {
  activeConnections: number;
  deviceCount: number;
  devices?: DeviceInfo[];
  capturedAuxbrain: number;
  passthrough: number;
  uniqueEndpoints: number;
  decryptOk: number;
  decryptErrors: number;
  lastError?: string;
  bytesCaptured: number;
  biggestEndpoint?: string;
  biggestEndpointBytes: number;
  certState?: string;
  running: boolean;
  port: number;
}

export interface DeviceInfo {
  ip?: string;
  hostname?: string;
  activeConnections: number;
  firstSeen?: string;
  lastSeen?: string;
  os?: string;
  gameVersion?: string;
  online: boolean;
  totalConnections: number;
}

export interface ObservedVersion {
  platform?: string;
  version?: string;
  build?: string;
  clientVersion?: number;
}

