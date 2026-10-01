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
      className={`rounded-xl border border-slate-200 bg-white p-5 shadow-sm sm:p-6 ${className}`}
    >
      {hasHeader && (
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div className="min-w-0">
            {eyebrow && <p className={eyebrowClassName}>{eyebrow}</p>}
            {title && (
              <Heading
                id={titleId}
                className={`break-words text-lg font-semibold tracking-tight text-slate-900 ${eyebrow ? 'mt-0.5' : ''}`}
              >
                {title}
              </Heading>
            )}
            {description && <p className="mt-1 text-sm text-slate-500">{description}</p>}
          </div>
          {actions && <div className="flex flex-wrap items-center gap-2">{actions}</div>}
        </div>
      )}
      {children && <div className={hasHeader ? 'mt-5' : ''}>{children}</div>}
    </section>
  );
}
