import { ChangeDetectionStrategy, Component, Pipe, PipeTransform, computed, input } from '@angular/core';

/** Small coloured status pill. Tone is derived from well-known status words. */
@Component({
  selector: 'app-status',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<span class="pill" [attr.data-tone]="tone()">{{ label() || value() }}</span>`,
})
export class StatusPill {
  value = input<string | undefined>('');
  label = input<string | undefined>(undefined);
  tone = computed(() => toneFor(this.value() ?? ''));
}

export function toneFor(v: string): string {
  const s = v.toUpperCase();
  if (['ACTIVE', 'APPROVED', 'PUBLISHED', 'VALIDATED', 'AUTOMATED', 'PASSED'].includes(s)) return 'good';
  if (['VALIDATIONFAILED', 'REJECTED', 'FAILED', 'NONE'].includes(s)) return 'bad';
  if (['INREVIEW', 'VALIDATING', 'UPLOADED', 'PARTIAL', 'IN_DEVELOPMENT', 'AUTOMATIONPLANNED', 'WARNING', 'PENDING'].includes(s)) return 'warn';
  if (['DEPRECATED', 'RETIRED', 'OBSOLETE', 'NOTAUTOMATABLE', 'SKIPPED'].includes(s)) return 'off';
  return 'neutral';
}

/** Inserts spaces into PascalCase enum values: "AutomationPlanned" → "Automation planned". */
@Pipe({ name: 'humanize' })
export class HumanizePipe implements PipeTransform {
  transform(v: string | undefined | null): string {
    if (!v) return '';
    const spaced = v.replace(/_/g, ' ').replace(/([a-z0-9])([A-Z])/g, '$1 $2').toLowerCase();
    return spaced.charAt(0).toUpperCase() + spaced.slice(1);
  }
}

@Pipe({ name: 'bytes' })
export class BytesPipe implements PipeTransform {
  transform(n: number | undefined | null): string {
    if (n == null) return '';
    if (n < 1024) return `${n} B`;
    if (n < 1024 * 1024) return `${(n / 1024).toFixed(1)} KB`;
    return `${(n / 1024 / 1024).toFixed(1)} MB`;
  }
}

/** Renders an audit ChangesJson payload as "Field: old → new" lines. */
@Pipe({ name: 'auditChanges' })
export class AuditChangesPipe implements PipeTransform {
  transform(json: string | undefined | null): { field: string; old?: string; new?: string }[] {
    if (!json) return [];
    try {
      const obj = JSON.parse(json) as Record<string, { old?: unknown; new?: unknown }>;
      return Object.entries(obj).map(([field, c]) => ({ field, old: fmt(c.old), new: fmt(c.new) }));
    } catch { return []; }
  }
}

function fmt(v: unknown): string | undefined {
  if (v === undefined) return undefined;
  if (v === null) return '∅';
  const s = typeof v === 'string' ? v : JSON.stringify(v);
  return s.length > 160 ? s.slice(0, 157) + '…' : s;
}
