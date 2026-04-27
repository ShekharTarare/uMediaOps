/**
 * Reusable notification helper for uMediaOps
 * Shows Umbraco-style notifications for success, error, and warning messages
 * Compatible with Umbraco 17
 */

import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification'

export class NotificationHelper {
  /**
   * Show a success notification
   * @param {Object} context - The component context (this)
   * @param {string} message - The success message to display
   */
  static async showSuccess(context, message) {
    try {
      const notificationContext = await context.getContext(
        UMB_NOTIFICATION_CONTEXT,
      )
      notificationContext.peek('positive', { data: { message } })
    } catch (error) {
      // Fallback if notification context not available
    }
  }

  /**
   * Show an error notification
   * @param {Object} context - The component context (this)
   * @param {string} message - The error message to display
   */
  static async showError(context, message) {
    try {
      const notificationContext = await context.getContext(
        UMB_NOTIFICATION_CONTEXT,
      )
      notificationContext.peek('danger', { data: { message } })
    } catch (error) {
      // Silently fail if notification context not available
    }
  }

  /**
   * Show a warning notification
   * @param {Object} context - The component context (this)
   * @param {string} message - The warning message to display
   */
  static async showWarning(context, message) {
    try {
      const notificationContext = await context.getContext(
        UMB_NOTIFICATION_CONTEXT,
      )
      notificationContext.peek('warning', { data: { message } })
    } catch (error) {
      // Silently fail if notification context not available
    }
  }

  /**
   * Show an info notification
   * @param {Object} context - The component context (this)
   * @param {string} message - The info message to display
   */
  static async showInfo(context, message) {
    try {
      const notificationContext = await context.getContext(
        UMB_NOTIFICATION_CONTEXT,
      )
      notificationContext.peek('default', { data: { message } })
    } catch (error) {
      // Silently fail if notification context not available
    }
  }
}
