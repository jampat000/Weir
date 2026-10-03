/**
 * The registry of Weir's periodic tasks: which exist, how each last went and when each next runs. It follows
 * Settings, so a workflow added or a task switched off appears or goes, and it tells its listeners whenever a task
 * starts or ends.
 */
import { wantedTasks } from "./definitions.mjs";

/** @typedef {import("./timed-task.mjs").TaskStatus & { key: string, label: string }} TaskRow */

/**
 * @typedef {object} Task
 * @property {(nowMs: number) => void} advance
 * @property {() => import("./timed-task.mjs").TaskStatus} status
 */

export class TaskBook {
  #context;
  /** @type {Map<string, { label: string, task: Task }>} */
  #tasks = new Map();
  #signature = "";
  /** @type {Set<(rows: TaskRow[]) => void>} */
  #listeners = new Set();

  /** @param {import("./definitions.mjs").TaskContext} context */
  constructor(context) {
    this.#context = context;
  }

  /** Calls `listener` with the whole list whenever a task starts or ends. @param {(rows: TaskRow[]) => void} listener */
  onChange(listener) {
    this.#listeners.add(listener);
    return () => this.#listeners.delete(listener);
  }

  /** Every task, in the order Weir lists them. */
  rows() {
    return [...this.#tasks].map(([key, { label, task }]) => ({
      key,
      label,
      ...task.status(),
    }));
  }

  /** Makes the registry hold the tasks Settings asks for: new ones start fresh, and ones that are gone are dropped. */
  #follow() {
    const followed = new Map();
    for (const { key, label, create } of wantedTasks(this.#context))
      followed.set(key, {
        label,
        task: this.#tasks.get(key)?.task ?? create(),
      });
    this.#tasks = followed;
  }

  /** @param {number} nowMs */
  advance(nowMs) {
    this.#follow();
    for (const { task } of this.#tasks.values()) task.advance(nowMs);
    const rows = this.rows();
    const signature = rows
      .map(
        (row) => `${row.key}|${row.running}|${row.last_run_at}|${row.last_ok}`,
      )
      .join(";");
    if (signature === this.#signature) return;
    this.#signature = signature;
    for (const listener of this.#listeners) listener(rows);
  }
}
