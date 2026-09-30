import axios from 'axios';
import { getAuthToken, clearAuthToken } from './auth';

export const API_BASE_URL = process.env.EXPO_PUBLIC_API_URL || 'https://api.stoictrade.in';

export const apiClient = axios.create({
  baseURL: API_BASE_URL,
  timeout: 10000,
  headers: {
    'Content-Type': 'application/json',
  },
});

apiClient.interceptors.request.use(async (config) => {
  const token = await getAuthToken();
  if (token) {
    config.headers.Authorization = `Bearer ${token}`;
  }
  return config;
}, (error) => {
  return Promise.reject(error);
});

apiClient.interceptors.response.use(
  (response) => response,
  async (error) => {
    if (error.response?.status === 401) {
      console.warn('API returned 401 Unauthorized - clearing session token');
      await clearAuthToken();
    }
    return Promise.reject(error);
  }
);
