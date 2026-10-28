export interface DiningTable {
  id: string;
  areaId: string;
  name: string;
  seats: number;
  sortOrder: number;
  isActive: boolean;
}

export interface DiningArea {
  id: string;
  name: string;
  sortOrder: number;
  isActive: boolean;
  tables: DiningTable[];
}

export interface KitchenLine {
  name: string;
  quantity: number;
  notes: string | null;
}

/** One round of an order on the kitchen screen. Send `etag` back as If-Match with a button. */
export interface KitchenTicket {
  id: string;
  orderId: string;
  orderNo: number;
  roundNo: number;
  channel: string;
  tableName: string | null;
  notes: string | null;
  /** Queued (shown as New), Preparing, Ready, Cancelled. */
  status: string;
  createdAtUtc: string;
  startedAtUtc: string | null;
  readyAtUtc: string | null;
  cancelledAtUtc: string | null;
  etag: string;
  lines: KitchenLine[];
}

export interface KitchenBoard {
  serverTimeUtc: string;
  tickets: KitchenTicket[];
}
