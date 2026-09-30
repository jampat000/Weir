import { Field } from "../../../../components/shared/field";
import { CONNECTION_NICKNAME_MAX_LENGTH } from "../../../../lib/ui/connection-title";

/**
 * The optional nickname on a media manager or download client, in the add and the edit form alike. It only ever adds
 * to the name Weir derives from where the connection runs, so it is never required.
 */
export function ConnectionNicknameField({
  value,
  onChange,
  testId,
  className,
}: {
  value: string;
  onChange: (value: string) => void;
  testId: string;
  /** The input's own class: the add forms and the edit forms style their inputs differently. */
  className: string;
}) {
  return (
    <Field
      label="Nickname (optional)"
      hint={`Shown after its name, such as “4K” or “Kids”. Up to ${CONNECTION_NICKNAME_MAX_LENGTH} characters.`}
      width="medium"
    >
      <input
        data-testid={testId}
        autoComplete="off"
        className={className}
        maxLength={CONNECTION_NICKNAME_MAX_LENGTH}
        value={value}
        onChange={(event) => onChange(event.target.value)}
      />
    </Field>
  );
}
