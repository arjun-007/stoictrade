import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  async rewrites() {
    // Determine the backend URL to proxy to. Default to local backend if NEXT_PUBLIC_API_URL is missing.
    // If NEXT_PUBLIC_API_URL is an empty string, it will fallback to localhost:5000.
    const backendUrl = process.env.NEXT_PUBLIC_API_URL || "http://localhost:5000";
    return [
      {
        source: '/api/:path*',
        destination: `${backendUrl}/api/:path*`,
      },
    ];
  },
};

export default nextConfig;
