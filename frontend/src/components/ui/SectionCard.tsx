import type { ReactNode } from 'react';
import { eyebrowClassName } from './fieldStyles';

interface SectionCardProps {
  /** Small uppercase line above the title. */
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
      className={`border border-slate-300 bg-white px-4 py-5 shadow-[4px_4px_0_rgba(15,23,42,0.06)] sm:px-6 sm:py-6 ${className}`}
    >
      {hasHeader && (
        <div className="flex flex-wrap items-end justify-between gap-3">
          <div className="min-w-0">
            {eyebrow && <p className={eyebrowClassName}>{eyebrow}</p>}
            {title && (
              <Heading
                id={titleId}
                className={`break-words text-xl font-semibold text-slate-900 ${eyebrow ? 'mt-1' : ''}`}
              >
                {title}
              </Heading>
            )}
            {description && <p className="mt-1 text-sm text-slate-600">{description}</p>}
          </div>
          {actions && <div className="flex flex-wrap gap-2">{actions}</div>}
        </div>
      )}
      {children && <div className={hasHeader ? 'mt-4' : ''}>{children}</div>}
    </section>
  );
}
