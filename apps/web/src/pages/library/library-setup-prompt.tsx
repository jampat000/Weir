import { useCanEdit } from "../../lib/auth/can-edit";
import { mmActionButtonClass } from "../../lib/ui/mm-control-roles";

/** What a library with no folders yet shows in place of its files: the one step that makes the page useful. */
export function LibrarySetupPrompt({
  libraryName,
  onSetUp,
}: {
  libraryName: string;
  onSetUp: () => void;
}) {
  const editable = useCanEdit();
  return (
    <section
      className="mm-library-empty mm-library-prompt"
      aria-labelledby="library-setup-prompt-title"
      data-testid="library-setup-prompt"
    >
      <h2 id="library-setup-prompt-title" className="mm-library-prompt__title">
        Set up this library
      </h2>
      <p>
        Tell Weir which folders hold the files {libraryName} already has. It
        reads them where they sit and shows what cleaning would change; nothing
        is touched until you ask.
      </p>
      {editable ? (
        <button
          type="button"
          className={mmActionButtonClass({ variant: "primary" })}
          onClick={onSetUp}
        >
          Set up this library
        </button>
      ) : (
        <p>An operator or an admin can set this library up.</p>
      )}
    </section>
  );
}
