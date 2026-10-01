import type { ReactNode } from 'react';
import { eyebrowClassName } from './fieldStyles';

interface SectionCardProps {
  /** Small line above the title. */
  eyebrow?: string;
  title?: ReactNode;
  titleId?: string;
  /** Use "h3" for a card nested under another section's h2. */
  headingLevel?: 'h2' | 'h3';
  description?: ReactNode;
  actions?: ReactNode;
  className?: string;
  children?: ReactNode;
}

// A panel with a header bar: the title and its actions sit above a rule, the content below.
export function SectionCard({
  eyebrow,
  title,
  titleId,
  headingLevel = 'h2',
  description,
  actions,
  className = '',
  children,
}: SectionCardProps) {
  const Heading = headingLevel;
  const hasHeader = Boolean(eyebrow || title || actions);

  return (
    <section
      aria-labelledby={title && titleId ? titleId : undefined}
      className={`rounded-lg border border-slate-200 bg-white ${className}`}
    >
      {hasHeader && (
        <div
          className={`flex flex-wrap items-center justify-between gap-x-4 gap-y-3 px-5 py-4 sm:px-6 ${
            children ? 'border-b border-slate-200' : ''
          }`}
        >
          <div className="min-w-0">
            {eyebrow && <p className={eyebrowClassName}>{eyebrow}</p>}
            {title && (
              <Heading id={titleId} className="break-words text-base font-semibold text-slate-900">
                {title}
              </Heading>
            )}
            {description && <p className="mt-0.5 text-sm text-slate-500">{description}</p>}
          </div>
          {actions && <div className="flex flex-wrap items-center gap-2">{actions}</div>}
        </div>
      )}
      {children && <div className="px-5 py-5 sm:px-6">{children}</div>}
    </section>
  );
}
