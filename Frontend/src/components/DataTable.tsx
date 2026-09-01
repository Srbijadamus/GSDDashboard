import { type ReactNode } from 'react'

export interface DataColumn<T> {
  key: string
  header: string
  render: (row: T, index: number) => ReactNode
  className?: string
  headerClassName?: string
}

interface DataTableProps<T> {
  columns: DataColumn<T>[]
  data: T[]
  rowKey: (row: T, index: number) => string | number
  className?: string
}

export function DataTable<T>({ columns, data, rowKey, className }: DataTableProps<T>) {
  return (
    <div className={['overflow-x-auto', className ?? ''].join(' ')}>
      <table className="w-full text-sm border-collapse">
        <thead>
          <tr className="border-b border-line-subtle bg-sunken">
            {columns.map(col => (
              <th
                key={col.key}
                className={[
                  'px-3 py-2 text-left text-2xs font-semibold uppercase tracking-wide text-ink-soft whitespace-nowrap',
                  col.headerClassName ?? '',
                ].join(' ')}
              >
                {col.header}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {data.map((row, i) => (
            <tr
              key={rowKey(row, i)}
              className="border-b border-line-subtle last:border-0 hover:bg-hovered transition-colors duration-fast"
            >
              {columns.map(col => (
                <td key={col.key} className={['px-3 py-2 align-middle', col.className ?? ''].join(' ')}>
                  {col.render(row, i)}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}
