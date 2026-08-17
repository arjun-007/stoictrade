import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  async rewrites() {
    return [
      {
        source: '/api/:path*',
        destination: 'http://165.232.189.55:5000/api/:path*',
      },
    ];
  },
};

export default nextConfig;
