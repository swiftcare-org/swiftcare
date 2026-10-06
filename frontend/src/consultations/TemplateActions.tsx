import { useState, type KeyboardEvent } from 'react';
import {
  createConsultationTemplate,
  removeConsultationTemplate,
  type ConsultationTemplate,
} from '../api/consultations';
import { ApiError } from '../api/client';
import { Banner } from '../components/ui/Banner';
import { Button } from '../components/ui/Button';
import { ConfirmPanel } from '../components/ui/ConfirmPanel';
import { Field } from '../components/ui/Field';
import { itemCardClassName } from '../components/ui/fieldStyles';

// Mirror CreateConsultationTemplateRequest in MedicalRecordService. The server remains
// authoritative; these only save a round trip for the obvious cases.
const NAME_MAX_LENGTH = 100;
const CLINICAL_TEXT_MAX_LENGTH = 5000;

export interface TemplateDraft {
  symptoms: string;
  examinationFindings: string;
  notes: string;
}

export interface TemplateDraftErrors {
  symptoms: string | null;
  examinationFindings: string | null;
  notes: string | null;
}

interface TemplateActionsProps {
  /** The clinical text currently in the consultation form: what a new template would hold. */
  draft: TemplateDraft;
  /** The template chosen in the dropdown, if any. */
  selectedTemplate: ConsultationTemplate | null;
  disabled: boolean;
  /** Problems with the draft are shown on the consultation form's own fields. */
  onDraftErrors: (errors: TemplateDraftErrors) => void;
  onSaved: (template: ConsultationTemplate) => void;
  onRemoved: (templateId: string) => void;
}

type OpenPanel = 'save' | 'remove' | null;

function draftFieldError(value: string, label: string): string | null {
  if (!value.trim()) {
    return `${label} are required to save a template`;
  }

  return value.length > CLINICAL_TEXT_MAX_LENGTH
    ? `${label} must be ${CLINICAL_TEXT_MAX_LENGTH} characters or fewer`
    : null;
}

export function TemplateActions({
  draft,
  selectedTemplate,
  disabled,
  onDraftErrors,
  onSaved,
  onRemoved,
}: TemplateActionsProps) {
  const [openPanel, setOpenPanel] = useState<OpenPanel>(null);
  const [name, setName] = useState('');
  const [nameError, setNameError] = useState<string | null>(null);
  const [panelError, setPanelError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);

  // Only a doctor's own template can be removed. Built-in ones never offer the option.
  const removable = selectedTemplate && !selectedTemplate.isBuiltIn ? selectedTemplate : null;

  function open(panel: Exclude<OpenPanel, null>) {
    setOpenPanel(panel);
    setName('');
    setNameError(null);
    setPanelError(null);
    setNotice(null);
  }

  function close() {
    setOpenPanel(null);
    setNameError(null);
    setPanelError(null);
  }

  async function handleSave() {
    const trimmedName = name.trim();
    const nextNameError = !trimmedName
      ? 'Template name is required'
      : trimmedName.length > NAME_MAX_LENGTH
        ? `Template name must be ${NAME_MAX_LENGTH} characters or fewer`
        : null;
    const draftErrors: TemplateDraftErrors = {
      symptoms: draftFieldError(draft.symptoms, 'Symptoms'),
      examinationFindings: draftFieldError(draft.examinationFindings, 'Examination findings'),
      notes: draftFieldError(draft.notes, 'Notes'),
    };

    setNameError(nextNameError);
    onDraftErrors(draftErrors);

    const draftIsInvalid = Object.values(draftErrors).some((error) => error !== null);
    setPanelError(
      draftIsInvalid ? 'Fill in symptoms, examination findings and notes below before saving a template.' : null,
    );

    if (nextNameError || draftIsInvalid) {
      return;
    }

    setBusy(true);

    try {
      const saved = await createConsultationTemplate({ name: trimmedName, ...draft });
      onSaved(saved);
      setOpenPanel(null);
      setNotice('Template saved');
    } catch (error) {
      if (error instanceof ApiError && error.status === 409) {
        setNameError('A template with this name already exists');
      } else if (error instanceof ApiError && error.status === 400 && Object.keys(error.fieldErrors).length > 0) {
        setNameError(error.fieldErrors.name ?? null);
        onDraftErrors({
          symptoms: error.fieldErrors.symptoms ?? null,
          examinationFindings: error.fieldErrors.examinationfindings ?? null,
          notes: error.fieldErrors.notes ?? null,
        });
      } else if (error instanceof ApiError && (error.status === 401 || error.status === 403)) {
        setPanelError('You are not authorized to save templates.');
      } else {
        setPanelError('Unable to save the template. Please try again.');
      }
    } finally {
      setBusy(false);
    }
  }

  // The panel sits inside the consultation form, so Enter must save the template
  // instead of submitting the consultation.
  function handleNameKeyDown(event: KeyboardEvent<HTMLInputElement>) {
    if (event.key === 'Enter') {
      event.preventDefault();
      void handleSave();
    }
  }

  async function handleRemove(template: ConsultationTemplate) {
    setBusy(true);
    setPanelError(null);

    try {
      await removeConsultationTemplate(template.id);
      onRemoved(template.id);
      setOpenPanel(null);
      setNotice('Template removed');
    } catch (error) {
      if (error instanceof ApiError && error.status === 404) {
        // Already gone, for example removed in another tab: the list is simply brought up to date.
        onRemoved(template.id);
        setOpenPanel(null);
        setNotice('Template removed');
      } else if (error instanceof ApiError && error.status === 403) {
        setPanelError('Built-in templates cannot be removed.');
      } else {
        setPanelError('Unable to remove the template. Please try again.');
      }
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="space-y-3" data-testid="template-actions">
      {openPanel === null && (
        <div className="flex flex-wrap gap-2">
          <Button
            variant="secondary"
            size="sm"
            disabled={disabled}
            onClick={() => open('save')}
            data-testid="save-as-template"
          >
            Save as template
          </Button>
          {removable && (
            <Button
              variant="secondary"
              size="sm"
              disabled={disabled}
              onClick={() => open('remove')}
              data-testid="remove-template"
            >
              Remove template
            </Button>
          )}
        </div>
      )}

      <div aria-live="polite" className="empty:hidden" data-testid="template-notice">
        {notice && openPanel === null && <Banner tone="success">{notice}</Banner>}
      </div>

      {openPanel === 'save' && (
        <div
          role="group"
          aria-labelledby="save-template-title"
          className={`${itemCardClassName} bg-slate-50 px-4 py-4`}
          data-testid="save-template-panel"
        >
          <p id="save-template-title" className="text-sm font-semibold text-slate-900">
            Save as Template
          </p>
          <p className="mt-0.5 text-sm text-slate-600">
            Saves the symptoms, examination findings and notes below for your own use. Other doctors will not
            see it.
          </p>

          {panelError && (
            <p role="alert" className="mt-3 text-sm font-medium text-red-700">
              {panelError}
            </p>
          )}

          <Field id="templateName" label="Template Name" required error={nameError} className="mt-4 max-w-md">
            {(control) => (
              <input
                {...control}
                name="templateName"
                type="text"
                autoComplete="off"
                maxLength={NAME_MAX_LENGTH}
                value={name}
                onChange={(event) => {
                  setName(event.target.value);
                  setNameError(null);
                }}
                onKeyDown={handleNameKeyDown}
                disabled={busy}
                autoFocus
              />
            )}
          </Field>

          <div className="mt-4 flex flex-wrap gap-2">
            <Button size="sm" loading={busy} onClick={() => void handleSave()} data-testid="save-template-confirm">
              {busy ? 'Saving…' : 'Save Template'}
            </Button>
            <Button variant="secondary" size="sm" disabled={busy} onClick={close}>
              Cancel
            </Button>
          </div>
        </div>
      )}

      {openPanel === 'remove' && removable && (
        <ConfirmPanel
          labelId="remove-template-title"
          title={`Remove "${removable.name}"?`}
          confirmLabel="Remove Template"
          busyLabel="Removing…"
          busy={busy}
          error={panelError}
          onConfirm={() => void handleRemove(removable)}
          onCancel={close}
        >
          It will no longer appear in your templates. Past consultations that used it are not affected.
        </ConfirmPanel>
      )}
    </div>
  );
}
