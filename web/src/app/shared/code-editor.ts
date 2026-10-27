import { ChangeDetectionStrategy, Component, ElementRef, OnDestroy, effect, input, output, viewChild } from '@angular/core';
import { defaultKeymap, history, historyKeymap, indentWithTab } from '@codemirror/commands';
import { javascript } from '@codemirror/lang-javascript';
import { json } from '@codemirror/lang-json';
import { python } from '@codemirror/lang-python';
import { xml } from '@codemirror/lang-xml';
import { yaml } from '@codemirror/lang-yaml';
import { bracketMatching, foldGutter, foldKeymap, indentOnInput, StreamLanguage, syntaxHighlighting, defaultHighlightStyle } from '@codemirror/language';
import { csharp } from '@codemirror/legacy-modes/mode/clike';
import { MergeView } from '@codemirror/merge';
import { highlightSelectionMatches, search, searchKeymap } from '@codemirror/search';
import { Compartment, EditorState, Extension } from '@codemirror/state';
import { EditorView, highlightActiveLine, highlightActiveLineGutter, keymap, lineNumbers } from '@codemirror/view';

function languageFor(lang: string): Extension {
  switch (lang) {
    case 'csharp': return StreamLanguage.define(csharp);
    case 'typescript': return javascript({ typescript: true });
    case 'javascript': return javascript();
    case 'python': return python();
    case 'json': return json();
    case 'yaml': return yaml();
    case 'xml': return xml();
    default: return [];
  }
}

function baseExtensions(lang: string, readOnly: boolean): Extension[] {
  return [
    lineNumbers(), highlightActiveLineGutter(), foldGutter(), history(), indentOnInput(), bracketMatching(), highlightActiveLine(),
    highlightSelectionMatches(), search({ top: true }), syntaxHighlighting(defaultHighlightStyle, { fallback: true }),
    keymap.of([indentWithTab, ...defaultKeymap, ...historyKeymap, ...searchKeymap, ...foldKeymap]),
    languageFor(lang), EditorState.readOnly.of(readOnly), EditorView.editable.of(!readOnly),
    EditorView.theme({ '&': { height: '100%', fontSize: '13px' }, '.cm-scroller': { fontFamily: 'var(--mono)' } }),
  ];
}

/**
 * CodeMirror 6 editor: syntax highlighting, line numbers, search/replace (Ctrl/Cmd+F), folding and undo history.
 * With `original` set it renders a side-by-side diff instead.
 */
@Component({
  selector: 'app-code-editor',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<div #host class="host"></div>`,
  styles: `:host { display: block; height: 100%; min-height: 200px; border: 1px solid var(--border); border-radius: 6px; overflow: hidden; }
           .host { height: 100%; } :host ::ng-deep .cm-mergeView { height: 100%; overflow: auto; }`,
})
export class CodeEditor implements OnDestroy {
  value = input<string>('');
  language = input<string>('plaintext');
  readOnly = input<boolean>(true);
  /** When provided, shows a read-only side-by-side diff: original (left) vs value (right). */
  original = input<string | undefined>(undefined);
  valueChange = output<string>();

  private host = viewChild.required<ElementRef<HTMLDivElement>>('host');
  private view?: EditorView;
  private merge?: MergeView;
  private readonlyCompartment = new Compartment();

  constructor() {
    effect(() => {
      const value = this.value(), lang = this.language(), readOnly = this.readOnly(), original = this.original();
      this.destroy();
      const parent = this.host().nativeElement;
      if (original !== undefined) {
        this.merge = new MergeView({
          a: { doc: original, extensions: baseExtensions(lang, true) },
          b: { doc: value, extensions: baseExtensions(lang, true) },
          parent, collapseUnchanged: { margin: 3, minSize: 6 },
        });
        return;
      }
      this.view = new EditorView({
        parent,
        state: EditorState.create({
          doc: value,
          extensions: [
            this.readonlyCompartment.of(baseExtensions(lang, readOnly)),
            EditorView.updateListener.of(u => { if (u.docChanged) this.valueChange.emit(u.state.doc.toString()); }),
          ],
        }),
      });
    });
  }

  current() { return this.view?.state.doc.toString() ?? this.value(); }

  ngOnDestroy() { this.destroy(); }

  private destroy() {
    this.view?.destroy(); this.view = undefined;
    this.merge?.destroy(); this.merge = undefined;
  }
}
