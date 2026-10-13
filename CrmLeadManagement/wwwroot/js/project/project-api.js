/**
 * Project API Client Module
 * Dedicated service for /api/projects endpoints
 */
const ProjectApi = (() => {
    const BASE_URL = '/api/projects';

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
                console.error(`[ProjectApi] Error on ${url}:`, error);
            }
            throw error;
        }
    }

    /**
     * Fetch all projects with optional query filters
     * @param {Object} [filters] - { status, projectType, search }
     * @returns {Promise<Array>} List of WorkProject objects
     */
    async function getAll(filters = {}) {
        const params = new URLSearchParams();
        if (filters.status) params.append('status', filters.status);
        if (filters.projectType) params.append('projectType', filters.projectType);
        if (filters.search) params.append('search', filters.search);

        const qs = params.toString();
        const url = qs ? `${BASE_URL}?${qs}` : BASE_URL;

        return await request(url, {
            method: 'GET',
            headers: { 'Accept': 'application/json' }
        });
    }

    /**
     * Fetch single project by ID with all relations
     * @param {number} id - Project ID
     * @returns {Promise<Object>} WorkProject object
     */
    async function getById(id) {
        const url = `${BASE_URL}/${id}`;
        return await request(url, {
            method: 'GET',
            headers: { 'Accept': 'application/json' }
        });
    }

    /**
     * Create a new project
     * @param {Object} payload - ProjectDto
     * @returns {Promise<Object>} Created WorkProject object
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
     * Update an existing project
     * @param {number} id - Project ID
     * @param {Object} payload - ProjectDto
     * @returns {Promise<Object>} Updated WorkProject object
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
     * Delete a project by ID
     * @param {number} id - Project ID
     * @returns {Promise<Object>} Response confirmation message
     */
    async function deleteProject(id) {
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
        delete: deleteProject
    };
})();
