/**
 * The regional language variants the server can match on (#496), so the language pickers can offer
 * a variant (`fre-CA`) beside its base language (`fre`). A rule naming a variant matches only it; a
 * rule naming the base language matches every variant. Kept in step by hand with
 * apps/server/src/Weir.Core/Rules/LanguageVariants.cs.
 */
export interface ProcessingLanguageVariantOption {
  /** The identifier a rule set stores, e.g. "fre-CA". */
  code: string;
  /** e.g. "French (Canada)". */
  label: string;
  /** The base language code this variant refines, e.g. "fre". */
  baseCode: string;
}

export const PROCESSING_LANGUAGE_VARIANT_OPTIONS: readonly ProcessingLanguageVariantOption[] =
  [
    { code: "fre-CA", label: "French (Canada)", baseCode: "fre" },
    { code: "fre-FR", label: "French (France)", baseCode: "fre" },
    { code: "fre-BE", label: "French (Belgium)", baseCode: "fre" },
    { code: "spa-ES", label: "Spanish (Spain)", baseCode: "spa" },
    { code: "spa-419", label: "Spanish (Latin America)", baseCode: "spa" },
    { code: "por-BR", label: "Portuguese (Brazil)", baseCode: "por" },
    { code: "por-PT", label: "Portuguese (Portugal)", baseCode: "por" },
    { code: "zho-Hant", label: "Chinese (Traditional)", baseCode: "zho" },
    { code: "zho-Hans", label: "Chinese (Simplified)", baseCode: "zho" },
    { code: "yue", label: "Cantonese", baseCode: "zho" },
    { code: "cmn", label: "Mandarin", baseCode: "zho" },
    { code: "nld-BE", label: "Flemish", baseCode: "nld" },
  ] as const;

/** Every variant declared for a given base language code, in table order. */
export function processingLanguageVariantsFor(
  baseCode: string,
): readonly ProcessingLanguageVariantOption[] {
  return PROCESSING_LANGUAGE_VARIANT_OPTIONS.filter(
    (v) => v.baseCode === baseCode,
  );
}
