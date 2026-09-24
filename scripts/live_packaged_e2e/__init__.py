"""Implementation package for ``scripts/live-packaged-e2e.py``.

Split out of that one script (#747) so each part stays under the project's line-count guideline.
The entry point script composes ``LiveAudit`` from the mixins here and is the only supported way
to run the audit; nothing in this package is imported from elsewhere.
"""
