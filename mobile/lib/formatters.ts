const MONTH_NAMES = ["JAN", "FEB", "MAR", "APR", "MAY", "JUN", "JUL", "AUG", "SEP", "OCT", "NOV", "DEC"];

function getLastThursdayOfMonth(year: number, month: number): number {
  const lastDay = new Date(year, month, 0).getDate();
  const date = new Date(year, month - 1, lastDay);
  const dayOfWeek = date.getDay();
  const diff = (dayOfWeek - 4 + 7) % 7;
  return lastDay - diff;
}

export function formatInstrumentName(symbol: string): string {
  if (!symbol || symbol === "-") return "-";
  let s = symbol.trim();
  if (s.startsWith("NSE:")) s = s.substring(4);
  if (s.startsWith("NIFTYNIFTY")) s = s.substring(5);

  if (!s.startsWith("NIFTY") || s.startsWith("NIFTYBANK") || s.startsWith("BANKNIFTY")) return s;
  const rest = s.substring(5); // after "NIFTY"

  const type = rest.endsWith("CE") ? "CE" : rest.endsWith("PE") ? "PE" : "";
  if (!type) return s;

  const noType = rest.substring(0, rest.length - 2);

  // Monthly format: e.g. "26AUG24000"
  const monthlyMatch = noType.match(/^(\d{2})([A-Za-z]{3})(\d+)$/);
  if (monthlyMatch) {
    const [, yy, mon, strike] = monthlyMatch;
    const monthIdx = MONTH_NAMES.indexOf(mon.toUpperCase());
    const fullYear = 2000 + parseInt(yy, 10);
    const lastThurs = monthIdx >= 0 ? getLastThursdayOfMonth(fullYear, monthIdx + 1) : 0;
    const dayStr = lastThurs > 0 ? `${String(lastThurs).padStart(2, "0")} ` : "";
    return `NIFTY ${dayStr}${mon.toUpperCase()} ${yy} ${strike} ${type}`;
  }

  // Weekly format: e.g. "2690824200" or "2682524200"
  const weeklyMatch = noType.match(/^(\d{2})([0-9ONDond])(\d{2})(\d+)$/);
  if (weeklyMatch) {
    const [, yy, mChar, dd, strike] = weeklyMatch;
    const mUpper = mChar.toUpperCase();
    let monthIdx = parseInt(mUpper, 10);
    if (mUpper === "O") monthIdx = 10;
    else if (mUpper === "N") monthIdx = 11;
    else if (mUpper === "D") monthIdx = 12;
    const mon = MONTH_NAMES[(monthIdx - 1) % 12] || mUpper;
    return `NIFTY ${dd} ${mon} ${yy} ${strike} ${type}`;
  }

  return s;
}
