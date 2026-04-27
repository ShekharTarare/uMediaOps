import { UMB_AUTH_CONTEXT } from '@umbraco-cms/backoffice/auth'

/**
 * AuthenticationHelper - Centralized authentication utility for uMediaOps components
 *
 * Handles token caching, expiration validation, refresh logic, and authenticated API requests
 * with automatic retry on 401 responses.
 *
 */
export class AuthenticationHelper {
  constructor(host) {
    this.host = host
    this.authContext = null
    this.cachedToken = null
    this.tokenExpiry = null
    this.REFRESH_THRESHOLD_MS = 5 * 60 * 1000 // 5 minutes
    this.initializationPromise = null
    this.refreshPromise = null // Promise-based locking for concurrent refresh
  }

  /**
   * Initialize the authentication helper by consuming UMB_AUTH_CONTEXT
   */
  async initialize() {
    // Prevent concurrent initialization
    if (this.initializationPromise) {
      return this.initializationPromise
    }

    this.initializationPromise = (async () => {
      try {
        if (!this.host) {
          throw new Error('Host element is required for authentication')
        }

        // Consume UMB_AUTH_CONTEXT - use callback-based approach
        // The callback will be called when context is available
        let resolved = false

        this.authContext = await new Promise((resolve, reject) => {
          const timeout = setTimeout(() => {
            if (!resolved) {
              resolved = true
              reject(new Error('Timeout waiting for UMB_AUTH_CONTEXT'))
            }
          }, 10000) // Increased to 10 seconds

          this.host.consumeContext(UMB_AUTH_CONTEXT, (context) => {
            if (!resolved && context) {
              resolved = true
              clearTimeout(timeout)
              resolve(context)
            }
          })
        })

        if (!this.authContext) {
          throw new Error('UMB_AUTH_CONTEXT not available')
        }
      } catch (error) {
        this.authContext = null
        throw new Error(
          `Authentication initialization failed: ${error.message}`,
        )
      }
    })()

    return this.initializationPromise
  }

  /**
   * Get a valid authentication token, refreshing if necessary
   */
  async getToken() {
    // Ensure initialization is complete
    if (!this.authContext) {
      await this.initialize()
    }

    // Check if cached token is still valid
    if (this.isTokenValid()) {
      return this.cachedToken
    }

    // Retrieve new token
    return await this.refreshToken()
  }

  /**
   * Check if the cached token is still valid
   */
  isTokenValid() {
    if (!this.cachedToken || !this.tokenExpiry) {
      return false
    }

    const now = Date.now()
    const timeUntilExpiry = this.tokenExpiry - now

    // Proactively refresh if within threshold (5 minutes)
    return timeUntilExpiry > this.REFRESH_THRESHOLD_MS
  }

  /**
   * Refresh the authentication token from UMB_AUTH_CONTEXT
   */
  async refreshToken() {
    // Prevent concurrent refresh - use promise-based locking
    if (this.refreshPromise) {
      return this.refreshPromise
    }

    this.refreshPromise = (async () => {
      try {
        if (!this.authContext) {
          throw new Error('Authentication context not initialized')
        }

        // Try multiple approaches to get the token
        let token = null

        // Approach 1: Try getLatestToken() method (Umbraco 14+)
        if (typeof this.authContext.getLatestToken === 'function') {
          try {
            token = await this.authContext.getLatestToken()
          } catch (e) {
            // Silently try next approach
          }
        }

        // Approach 2: Try getOpenApiConfiguration() if available
        if (
          !token &&
          typeof this.authContext.getOpenApiConfiguration === 'function'
        ) {
          try {
            const config = this.authContext.getOpenApiConfiguration()
            if (config && typeof config.token === 'function') {
              token = await config.token()
            }
          } catch (e) {
            // Silently try next approach
          }
        }

        // Defensive validation - Requirements 8.2, 8.7
        if (!token || token.trim() === '') {
          throw new Error('Token unavailable or empty')
        }

        this.cachedToken = token
        // Parse JWT to get expiration
        this.tokenExpiry = this.parseTokenExpiry(token)

        return this.cachedToken
      } catch (error) {
        this.cachedToken = null
        this.tokenExpiry = null
        throw new Error(`Token refresh failed: ${error.message}`)
      } finally {
        // Clear the refresh promise so future refreshes can proceed
        this.refreshPromise = null
      }
    })()

    return this.refreshPromise
  }

  /**
   * Parse JWT token to extract expiration time
   */
  parseTokenExpiry(token) {
    try {
      // JWT tokens are base64 encoded: header.payload.signature
      const parts = token.split('.')
      if (parts.length !== 3) {
        // Not a JWT token, use default expiration (5 minutes to match Umbraco's default)
        return Date.now() + 5 * 60 * 1000
      }

      const payload = parts[1]
      const decoded = JSON.parse(atob(payload))

      // 'exp' claim is in seconds, convert to milliseconds
      if (decoded.exp && typeof decoded.exp === 'number') {
        return decoded.exp * 1000
      }

      // If no exp claim, assume 5 minutes validity (Umbraco default)
      return Date.now() + 5 * 60 * 1000
    } catch (error) {
      // If parsing fails, assume 5 minutes validity as fallback (Umbraco default)
      return Date.now() + 5 * 60 * 1000
    }
  }

  /**
   * Make an authenticated API request with automatic retry on 401
   */
  async makeAuthenticatedRequest(url, options = {}) {
    try {
      const token = await this.getToken()

      // Merge headers properly - don't let custom headers overwrite Authorization
      const headers = {
        'Content-Type': 'application/json',
        ...options.headers, // Custom headers first
        Authorization: `Bearer ${token}`, // Authorization last to prevent overwriting
      }

      const response = await fetch(url, {
        ...options,
        headers,
      })

      // Handle 401 by refreshing token and retrying once
      if (response.status === 401) {
        try {
          const newToken = await this.refreshToken()

          // Retry with new token
          const retryHeaders = {
            'Content-Type': 'application/json',
            ...options.headers,
            Authorization: `Bearer ${newToken}`,
          }

          const retryResponse = await fetch(url, {
            ...options,
            headers: retryHeaders,
          })

          // If retry also returns 401, throw error
          if (retryResponse.status === 401) {
            throw new Error(
              'Authentication failed after token refresh. Please ensure you are logged into the Umbraco backoffice.',
            )
          }

          return retryResponse
        } catch (refreshError) {
          // If refresh fails, throw with clear message
          throw new Error(
            `Token refresh failed after 401: ${refreshError.message}`,
          )
        }
      }

      return response
    } catch (error) {
      // Handle network errors and other failures
      if (
        error.message.includes('Token refresh failed') ||
        error.message.includes('Authentication failed')
      ) {
        throw error
      }
      throw new Error(`Authentication request failed: ${error.message}`)
    }
  }

  /**
   * Clear cached token (called on component destruction or session end)
   */
  clearToken() {
    this.cachedToken = null
    this.tokenExpiry = null
  }

  /**
   * Cancel any pending operations (called on component destruction)
   */
  destroy() {
    this.clearToken()
    this.authContext = null
    this.initializationPromise = null
    this.refreshPromise = null
  }
}
