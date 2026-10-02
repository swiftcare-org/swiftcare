import { Link } from 'react-router-dom';
import { Icon } from './Icon';

interface BackLinkProps {
  to: string;
  /** The destination's name, e.g. "Dashboard". Rendered as "Back to Dashboard". */
  destination: string;
}

export function BackLink({ to, destination }: BackLinkProps) {
  return (
    <Link
      to={to}
      className="inline-flex items-center gap-1.5 rounded text-sm font-medium text-slate-500 hover:text-slate-900 focus:outline-none focus-visible:ring-2 focus-visible:ring-brand-blue/40 focus-visible:ring-offset-2"
    >
      <Icon name="arrowLeft" className="h-4 w-4" />
      Back to {destination}
    </Link>
  );
}
