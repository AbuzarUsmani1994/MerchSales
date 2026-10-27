/// Mobile numbers as the server matches members: digits only, country code first, so
/// "0300-1234567", "03001234567" and "+92 300 1234567" are the same customer (923001234567).
/// Mirrors PhoneNumbers.Normalize on the server; null when it isn't a usable number.
String? normalizePhone(String? input) {
  if (input == null || input.trim().isEmpty) return null;
  if (RegExp(r'[^0-9 +()\-]').hasMatch(input)) return null;
  var international = input.trimLeft().startsWith('+');
  var digits = input.replaceAll(RegExp(r'[^0-9]'), '');
  if (digits.startsWith('00')) {
    digits = digits.substring(2);
    international = true;
  }

  if (!international && digits.length == 11 && digits.startsWith('0')) {
    digits = '92${digits.substring(1)}';
  } else if (!international && digits.length == 10 && digits.startsWith('3')) {
    digits = '92$digits';
  } else if (!international && !(digits.length == 12 && digits.startsWith('92'))) {
    return null;
  }

  return digits.length >= 8 && digits.length <= 15 ? digits : null;
}
