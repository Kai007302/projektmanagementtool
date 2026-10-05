import { priorityEmoji, taskPriorities, type TaskPriority } from './api'

/** Priority as a colored chip with emoji; the label stays as text for screen readers. */
export function PriorityBadge({ priority }: { priority: TaskPriority }) {
  return (
    <span className={`priority-badge priority-${priority}`}>
      <span aria-hidden="true">{priorityEmoji[priority]}</span> {taskPriorities[priority]}
    </span>
  )
}
