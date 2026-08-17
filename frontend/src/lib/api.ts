export async function fetchWithAuth(url: string, options: RequestInit = {}) {
  // If we are on the server (SSR), localStorage is not available.
  // This app mostly uses Client Components ("use client") for fetching data.
  let token = "";
  if (typeof window !== "undefined") {
    token = localStorage.getItem("jwt_token") || "";
  }

  const headers = new Headers(options.headers);
  if (token) {
    headers.set("Authorization", `Bearer ${token}`);
  }

  const baseUrl = process.env.NEXT_PUBLIC_API_URL || "";
  const fullUrl = url.startsWith("/") ? `${baseUrl}${url}` : url;

  const res = await fetch(fullUrl, {
    ...options,
    headers,
  });

  if (res.status === 401) {
    // Unauthorized: token missing or invalid
    if (typeof window !== "undefined") {
      localStorage.removeItem("jwt_token");
      if (window.location.pathname !== "/login") {
        window.location.href = "/login";
      }
    }
  }

  return res;
}
