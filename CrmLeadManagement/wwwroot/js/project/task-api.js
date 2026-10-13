/**
 * Task API Client Module
 * Dedicated service for /api/tasks endpoints
 */
const TaskApi = (() => {
    const BASE_URL = '/api/tasks';

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
                console.error(`[TaskApi] Error on ${url}:`, error);
            }
            throw error;
        }
    }

    /**
     * Fetch all tasks with optional query filters
     * @param {Object} [filters] - { projectId, milestoneId, status, priority, assignedMember, search }
     * @returns {Promise<Array>} List of TaskResponseDto objects
     */
    async function getAll(filters = {}) {
        const params = new URLSearchParams();
        if (filters.projectId) params.append('projectId', filters.projectId);
        if (filters.milestoneId) params.append('milestoneId', filters.milestoneId);
        if (filters.status) params.append('status', filters.status);
        if (filters.priority) params.append('priority', filters.priority);
        if (filters.assignedMember) params.append('assignedMember', filters.assignedMember);
        if (filters.search) params.append('search', filters.search);

        const qs = params.toString();
        const url = qs ? `${BASE_URL}?${qs}` : BASE_URL;

        return await request(url, {
            method: 'GET',
            headers: { 'Accept': 'application/json' }
        });
    }

    /**
     * Fetch a task by ID
     * @param {number} id - Task ID
     * @returns {Promise<Object>} TaskResponseDto
     */
    async function getById(id) {
        const url = `${BASE_URL}/${id}`;
        return await request(url, {
            method: 'GET',
            headers: { 'Accept': 'application/json' }
        });
    }

    /**
     * Create a new task
     * @param {Object} payload - TaskDto
     * @returns {Promise<Object>} Created task
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
     * Update an existing task
     * @param {number} id - Task ID
     * @param {Object} payload - TaskDto
     * @returns {Promise<Object>} Updated task
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
     * Patch status or completion amount of a task
     * @param {number} id - Task ID
     * @param {Object} payload - { status, completedAmount }
     * @returns {Promise<Object>} Updated status response
     */
    async function updateStatus(id, payload) {
        const url = `${BASE_URL}/${id}/status`;
        return await request(url, {
            method: 'PATCH',
            headers: {
                'Content-Type': 'application/json',
                'Accept': 'application/json'
            },
            body: JSON.stringify(payload)
        });
    }

    /**
     * Delete a task by ID
     * @param {number} id - Task ID
     * @returns {Promise<Object>} Response confirmation message
     */
    async function deleteTask(id) {
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
        updateStatus,
        delete: deleteTask
    };
})();
