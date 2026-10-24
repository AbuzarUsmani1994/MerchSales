/// Section 12 offline rule: new transactions are blocked when the last successful sync is
/// older than the tenant's Mobile.MaxOfflineHours. A phone that has never synced is blocked
/// too — it has no prices, stock or permissions to work from yet.
class OfflinePolicy {
  const OfflinePolicy({required this.lastSuccessfulSyncAtUtc, required this.maxOfflineHours});

  final DateTime? lastSuccessfulSyncAtUtc;
  final int maxOfflineHours;

  bool allowsNewTransactions(DateTime nowUtc) {
    final last = lastSuccessfulSyncAtUtc;
    if (last == null) return false;
    return nowUtc.difference(last) <= Duration(hours: maxOfflineHours);
  }
}
