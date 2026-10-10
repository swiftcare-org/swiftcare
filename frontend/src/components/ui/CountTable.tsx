import { numericClassName } from './fieldStyles';
import {
  tableBodyClassName,
  tableCellClassName,
  tableClassName,
  tableHeadClassName,
  tableHeaderCellClassName,
  tableKeyCellClassName,
  tableWrapperClassName,
} from './table';

export interface CountRow {
  /** Unique within the table. */
  key: string;
  label: string;
  count: number;
}

interface CountTableProps {
  /** What the rows are, for example "Room". */
  labelHeading: string;
  /** What is counted, for example "Patients". */
  countHeading: string;
  rows: CountRow[];
  testId?: string;
}

// A two-column table of things and how many of each: rooms, weeks, diagnoses.
export function CountTable({ labelHeading, countHeading, rows, testId }: CountTableProps) {
  return (
    <div className={tableWrapperClassName}>
      <table className={tableClassName} data-testid={testId}>
        <thead className={tableHeadClassName}>
          <tr>
            <th scope="col" className={tableHeaderCellClassName}>
              {labelHeading}
            </th>
            <th scope="col" className={tableHeaderCellClassName}>
              {countHeading}
            </th>
          </tr>
        </thead>
        <tbody className={tableBodyClassName}>
          {rows.map((row) => (
            <tr key={row.key}>
              <td className={`${tableKeyCellClassName} break-words`}>{row.label}</td>
              <td className={`${tableCellClassName} ${numericClassName}`}>{row.count}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
