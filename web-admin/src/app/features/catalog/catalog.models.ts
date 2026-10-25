export interface LookupItem {
  id: string;
  code: string;
  name: string;
}

export interface CategoryItem {
  id: string;
  name: string;
  sortOrder: number;
  isActive: boolean;
}

export interface CatalogLookups {
  units: LookupItem[];
  productTypes: LookupItem[];
  categories: CategoryItem[];
  hasDefaultPriceList: boolean;
}

/** One row of GET /catalog/products — prices as charged at the requested branch. */
export interface MenuItem {
  id: string;
  sku: string;
  name: string;
  shortName: string | null;
  description: string | null;
  categoryId: string | null;
  categoryName: string | null;
  productTypeId: string;
  productTypeName: string;
  uomId: string;
  uomCode: string;
  /** null = on the shared menu of every branch. */
  ownerBranchId: string | null;
  isActive: boolean;
  basePrice: number | null;
  branchPrice: number | null;
  price: number | null;
  isAvailable: boolean;
  canEdit: boolean;
  canSetBranchPricing: boolean;
  /** The item's picture, or null for the placeholder. Changes whenever the picture is replaced. */
  imageAttachmentId: string | null;
}

/** Sentinel for "the shared menu, no branch" in selects (mat-select can't use null reliably). */
export const SHARED_MENU = '__shared__';
