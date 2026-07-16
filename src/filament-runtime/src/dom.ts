import { stats } from './stats';

/* ===========================================================================
 * DOM ops.
 *
 * These are DUMB. Not one of them tracks, subscribes, diffs, or remembers a
 * previous value. Reactivity is composed by the caller:
 *
 *     effect(() => setText(t, count.value))
 *
 * That is the entire thesis. There is no render tree to allocate, no vdom to
 * diff: the effect closed over `t` at CREATE time, so an increment is a flag
 * walk plus one `t.data = v`. Exactly one DOM write. (C3.)
 *
 * Deliberately NOT here: a "skip the write if the value is unchanged" guard.
 * It would cost a DOM read (`node.data`) on every update to prevent a write the
 * effect graph has already proven necessary — the effect only re-ran because a
 * dependency's value actually changed. Paying a read to avoid a write we want is
 * a bad trade, and it would make C3's "exactly 1" harder to reason about, not
 * easier.
 * =========================================================================== */

/**
 * Write text.
 *
 * `node` is a Text node, not an Element: `.data` is a direct character-data
 * store, whereas `el.textContent = v` destroys and rebuilds the element's
 * children. A compiler emitting `<td>@row.Id</td>` creates the Text node once at
 * create time and hands it here forever.
 *
 * `v` is `unknown` because the DOM coerces anyway, and forcing the caller to
 * pre-stringify would move an allocation into generated app code without
 * removing it.
 */
export function setText(node: Text, v: unknown): void {
  if (__FILAMENT_STATS__) stats.text++;
  node.data = v as string;
}

/** Set an attribute. null/undefined removes it — that is how a compiler expresses "absent". */
export function setAttr(el: Element, name: string, v: unknown): void {
  if (__FILAMENT_STATS__) stats.attr++;
  if (v == null) el.removeAttribute(name);
  else el.setAttribute(name, v as string);
}

/**
 * Attach an event listener.
 *
 * No delegation, no synthetic event system, no per-node handler registry. A
 * listener on a node that is later removed dies with the node; that is the
 * platform's job and it already does it.
 */
export function listen(el: Element, evt: string, fn: EventListener): void {
  if (__FILAMENT_STATS__) stats.listen++;
  el.addEventListener(evt, fn);
}

/** Insert `node` before `anchor` (append when anchor is null/undefined). */
export function insert(parent: Node, node: Node, anchor?: Node | null): void {
  if (__FILAMENT_STATS__) stats.insert++;
  parent.insertBefore(node, anchor ?? null);
}

/** Detach `node` from its parent. */
export function remove(node: ChildNode): void {
  if (__FILAMENT_STATS__) stats.remove++;
  node.remove();
}
