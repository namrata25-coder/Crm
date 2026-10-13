/**
 * Milestone API Client Module
 * Dedicated service for /api/milestones endpoints
 */
const MilestoneApi = (() => {
    const BASE_URL = '/api/milestones';

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
                console.error(`[MilestoneApi] Error on ${url}:`, error);
            }
            throw error;
        }
    }

    /**
     * Fetch all milestones with optional query filters
     * @param {Object} [filters] - { projectId, status, search }
     * @returns {Promise<Array>} List of MilestoneResponseDto objects
     */
    async function getAll(filters = {}) {
        const params = new URLSearchParams();
        if (filters.projectId) params.append('projectId', filters.projectId);
        if (filters.status) params.append('status', filters.status);
        if (filters.search) params.append('search', filters.search);

        const qs = params.toString();
        const url = qs ? `${BASE_URL}?${qs}` : BASE_URL;

        return await request(url, {
            method: 'GET',
            headers: { 'Accept': 'application/json' }
        });
    }

    /**
     * Fetch a milestone by ID
     * @param {number} id - Milestone ID
     * @returns {Promise<Object>} MilestoneResponseDto
     */
    async function getById(id) {
        const url = `${BASE_URL}/${id}`;
        return await request(url, {
            method: 'GET',
            headers: { 'Accept': 'application/json' }
        });
    }

    /**
     * Create a new milestone
     * @param {Object} payload - MilestoneDto
     * @returns {Promise<Object>} Created milestone
     */
    async function create(payload) {
        const url = BASE_URL;
        return await request(url, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'Accept': 'application/json'
            },
            body: JSON.stringify(payload)
        });
    }

    /**
     * Update an existing milestone
     * @param {number} id - Milestone ID
     * @param {Object} payload - MilestoneDto
     * @returns {Promise<Object>} Updated milestone
     */
    async function update(id, payload) {
        const url = `${BASE_URL}/${id}`;
        return await request(url, {
            method: 'PUT',
            headers: {
                'Content-Type': 'application/json',
                'Accept': 'application/json'
            },
            body: JSON.stringify(payload)
        });
    }

    /**
     * Delete a milestone by ID
     * @param {number} id - Milestone ID
     * @returns {Promise<Object>} Response confirmation message
     */
    async function deleteMilestone(id) {
        const url = `${BASE_URL}/${id}`;
        return await request(url, {
            method: 'DELETE',
            headers: { 'Accept': 'application/json' }
        });
    }

    return {
        getAll,
        getById,
        create,
        update,
        delete: deleteMilestone
    };
})();
