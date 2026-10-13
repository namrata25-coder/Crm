/**
 * Work API Client Module
 * Dedicated service for /api/work endpoints
 */
const WorkApi = (() => {
    const BASE_URL = '/api/work';

    async function request(url, options = {}) {
        options.credentials = 'same-origin';
        try {
            const response = await fetch(url, options);
            if (response.status === 401) {
                window.location.href = '/login?returnUrl=' + encodeURIComponent(window.location.pathname + window.location.search);
                throw new Error('Unauthorized');
            }
            if (!response.ok) {
                const errText = await response.text();
                throw new Error(`${options.method || 'GET'} ${url} failed [${response.status}]: ${errText || response.statusText}`);
            }
            return await response.json();
        } catch (error) {
            if (error.message !== 'Unauthorized') {
                console.error(`[WorkApi] Error on ${url}:`, error);
            }
            throw error;
        }
    }

    /**
     * Fetch high-level statistics and metrics for the Work module
     * @returns {Promise<Object>} WorkSummaryDto
     */
    async function getSummary() {
        const url = `${BASE_URL}/summary`;
        return await request(url, {
            method: 'GET',
            headers: { 'Accept': 'application/json' }
        });
    }

    /**
     * Fetch aggregated report data across projects
     * @param {number|null} projectId - Optional filter by project ID
     * @returns {Promise<Array>} List of project reports
     */
    async function getReports(projectId = null) {
        const url = projectId ? `${BASE_URL}/reports?projectId=${encodeURIComponent(projectId)}` : `${BASE_URL}/reports`;
        return await request(url, {
            method: 'GET',
            headers: { 'Accept': 'application/json' }
        });
    }

    return {
        getSummary,
        getReports
    };
})();
