/**
 * The path a delivered poster takes from its card to the shelf. The two are both 2:3, so the poster scales by one
 * factor, taken from its width, and never changes shape. It rises a little as it leaves, bows to one side on the
 * way, and is placed against the shelf tile's place as it is now, so it lands where the tile is even if the shelf
 * has moved since take-off.
 */

/** How long the flight lasts, and how long the poster then takes to give way to the shelf's own tile. */
export const FLIGHT_MS = 1000;
export const LANDING_FADE_MS = 150;

/** The most the poster grows by at the height of its lift. */
const LIFT_SCALE = 0.04;
/** How far the path bows from a straight line, as a share of the distance flown. */
const ARC_SHARE = 0.12;

/** Where a poster is on screen: its top-left corner and its width. */
export type Spot = { left: number; top: number; width: number };

/** Where the poster's top-left corner is, and how much it is scaled by, at one moment of the flight. */
export type Pose = { x: number; y: number; scale: number };

/** Slow at both ends, quick between: the curve of cubic-bezier(.45, .05, .25, 1) without the table. */
export function easeInOut(progress: number): number {
  return progress < 0.5 ? 4 * progress ** 3 : 1 - (-2 * progress + 2) ** 3 / 2;
}

/** The side of the path the bow goes to: always rightwards, so it does not flip as the target moves. */
function bowDirection(dx: number, dy: number): { x: number; y: number } {
  const distance = Math.hypot(dx, dy);
  if (distance < 1) return { x: 0, y: 0 };
  const side = dy >= 0 ? 1 : -1;
  return { x: (side * dy) / distance, y: (-side * dx) / distance };
}

/**
 * Where the poster is `progress` (0 to 1, after easing) of the way along its flight. The poster is laid out at its
 * card size with its top-left corner as the origin of the transform, so the lift is taken off the corner to keep the
 * poster growing from its centre.
 */
export function poseAt(
  progress: number,
  from: Spot & { height: number },
  to: Spot,
): Pose {
  const dx = to.left - from.left;
  const dy = to.top - from.top;
  const bow = bowDirection(dx, dy);
  const bend = Math.hypot(dx, dy) * ARC_SHARE;
  const controlX = from.left + dx / 2 + bow.x * bend;
  const controlY = from.top + dy / 2 + bow.y * bend;
  const rest = 1 - progress;
  const left =
    rest * rest * from.left +
    2 * rest * progress * controlX +
    progress * progress * to.left;
  const top =
    rest * rest * from.top +
    2 * rest * progress * controlY +
    progress * progress * to.top;
  const size = 1 + (to.width / from.width - 1) * progress;
  const lift = 1 + LIFT_SCALE * Math.sin(Math.PI * progress);
  return {
    x: left - (from.width * size * (lift - 1)) / 2,
    y: top - (from.height * size * (lift - 1)) / 2,
    scale: size * lift,
  };
}
