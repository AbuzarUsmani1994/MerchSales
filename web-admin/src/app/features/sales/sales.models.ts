export interface SalesOrderLine {
  id: string;
  lineNumber: number;
  productId: string;
  productName: string;
  quantity: number;
  unitPrice: number;
  lineTotal: number;
  notes: string | null;
}

/** One row of GET /sales/orders. Send `etag` back as If-Match on actions. */
export interface SalesOrder {
  id: string;
  documentNo: string | null;
  status: string;
  channel: string;
  branchId: string;
  branchName: string;
  orderDate: string;
  businessDate: string;
  /** Who it's for, typed by the waiter: "Table 4", "Ali, takeaway". */
  notes: string | null;
  takenBy: string | null;
  subTotal: number;
  discountTotal: number;
  taxTotal: number;
  grandTotal: number;
  paidAmount: number;
  etag: string;
  lines: SalesOrderLine[];
}

export interface PagedList<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export interface PaymentMethod {
  id: string;
  code: string;
  name: string;
  /** Cash, Card, BankTransfer, Wallet, Credit, Cheque. */
  kind: string;
}

export interface CashSession {
  id: string;
  branchId: string;
  businessDate: string;
  status: string;
  openedAtUtc: string;
  openingFloat: number;
  paymentCount: number;
  cashTaken: number;
  otherTaken: number;
  expectedCash: number;
  countedCash: number | null;
  closedAtUtc: string | null;
}

export interface CompletedSale {
  order: SalesOrder;
  invoiceNo: string;
  paymentNo: string | null;
  paymentMethod: string | null;
  amountPaid: number;
  change: number;
}
