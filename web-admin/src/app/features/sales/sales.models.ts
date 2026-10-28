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
  /** A visit-reward member's phone the waiter noted ("0300 1234567"). */
  loyaltyPhone: string | null;
  etag: string;
  lines: SalesOrderLine[];
  /** The restaurant's short daily number (#23); null where the kitchen screen is off. */
  orderNo: number | null;
  tableId: string | null;
  tableName: string | null;
  covers: number | null;
  /** The waiter said the table wants its bill. */
  billRequested: boolean;
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
  loyalty: LoyaltyOutcome | null;
  /** All the cash handed over (split bills too), for the receipt's first print. */
  cashTendered: number | null;
}

/** One part of a split bill: the parts must add up to the total. */
export interface PaymentPart {
  paymentMethodId: string;
  amount: number;
  tendered: number | null;
  reference: string | null;
}

export interface LoyaltyRules {
  visitsForReward: number;
  minVisitBill: number;
  rewardAmount: number;
  minRewardBill: number;
  expiryMonths: number;
}

/** Visit rewards at the till's branch, before any phone is typed. */
export interface LoyaltyBranchStatus {
  programmeOn: boolean;
  canAddMember: boolean;
  rules: LoyaltyRules;
}

export interface LoyaltyLookup {
  phone: string;
  programmeOn: boolean;
  isMember: boolean;
  memberId: string | null;
  name: string | null;
  status: string | null;
  visits: number;
  visitsForReward: number;
  rewardAvailable: boolean;
  countedToday: boolean;
  rewardAmount: number;
  minRewardBill: number;
  minVisitBill: number;
  canAddMember: boolean;
}

export interface LoyaltyOutcome {
  phone: string;
  name: string | null;
  visitCounted: boolean;
  rewardUsed: boolean;
  rewardAmount: number;
  visits: number;
  visitsForReward: number;
  rewardAvailable: boolean;
  message: string | null;
}

export interface LoyaltyMemberRow {
  id: string;
  phone: string;
  name: string;
  status: string;
  visits: number;
  visitsForReward: number;
  rewardAvailable: boolean;
  rewardsUsed: number;
  lastVisit: string | null;
  joinedAtUtc: string;
  joinedBy: string | null;
  joinedBranch: string | null;
}

export interface LoyaltyBranchSwitches {
  branchId: string;
  branchName: string;
  isEnabled: boolean;
  cashierCanAddMembers: boolean;
}

export interface LoyaltySettings {
  rules: LoyaltyRules;
  canEditRules: boolean;
  branches: LoyaltyBranchSwitches[];
}
