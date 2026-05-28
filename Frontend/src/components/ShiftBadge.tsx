const colors: Record<string, string> = {
  WORKING:     "bg-green-100 text-green-800 dark:bg-green-900/40 dark:text-green-300",
  WIC_DUTY:    "bg-cyan-100 text-cyan-800 dark:bg-cyan-900/40 dark:text-cyan-300",
  AL:          "bg-blue-100 text-blue-800 dark:bg-blue-900/40 dark:text-blue-300",
  HALF_AL:     "bg-blue-50 text-blue-700 dark:bg-blue-950/40 dark:text-blue-400",
  SL:          "bg-orange-100 text-orange-800 dark:bg-orange-900/40 dark:text-orange-300",
  UL:          "bg-red-100 text-red-800 dark:bg-red-900/40 dark:text-red-300",
  TRAINING:    "bg-purple-100 text-purple-800 dark:bg-purple-900/40 dark:text-purple-300",
  OFF:         "bg-gray-100 text-gray-600 dark:bg-gray-800 dark:text-gray-400",
  OFF_WEEKEND: "bg-gray-50 text-gray-500 dark:bg-gray-900 dark:text-gray-500",
  PH:          "bg-yellow-100 text-yellow-800 dark:bg-yellow-900/40 dark:text-yellow-300",
  LPH:         "bg-yellow-50 text-yellow-700 dark:bg-yellow-950/40 dark:text-yellow-400",
  CD:          "bg-white text-gray-700 border border-gray-300 dark:bg-gray-900 dark:text-gray-300",
  CO:          "bg-white text-gray-700 border border-gray-300 dark:bg-gray-900 dark:text-gray-300",
  RESIGNED:    "bg-gray-200 text-gray-500 dark:bg-gray-800 dark:text-gray-600 line-through",
  EMPTY:       "bg-transparent text-gray-300 dark:text-gray-700",
}

export function ShiftBadge({ type, time }: { type: string; time?: string }) {
  const cls = colors[type] ?? colors.EMPTY
  return (
    <span className={`inline-flex items-center px-2 py-0.5 rounded text-xs font-medium ${cls}`}>
      {time ?? type}
    </span>
  )
}
