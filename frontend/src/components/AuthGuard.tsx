"use client";

import { useEffect, useState } from "react";
import { usePathname, useRouter } from "next/navigation";
import Navigation from "@/components/Navigation";

export default function AuthGuard({ children }: { children: React.ReactNode }) {
  const [isAuthenticated, setIsAuthenticated] = useState<boolean | null>(null);
  const pathname = usePathname();
  const router = useRouter();

  useEffect(() => {
    const token = localStorage.getItem("jwt_token");
    if (!token) {
      if (pathname !== "/login") {
        router.replace("/login");
      } else {
        setIsAuthenticated(false);
      }
    } else {
      setIsAuthenticated(true);
    }
  }, [pathname, router]);

  // Prevent rendering flash
  if (isAuthenticated === null) {
    return <div className="min-h-screen flex items-center justify-center bg-slate-50 dark:bg-[#0B1121]"></div>; 
  }

  // Render Login page without Navigation bar
  if (pathname === "/login") {
    return <main className="flex-1 w-full overflow-y-auto">{children}</main>;
  }

  // Render Dashboard with Navigation bar
  return (
    <>
      <Navigation />
      <main className="flex-1 w-full pb-20 md:pb-0 overflow-y-auto">
        {children}
      </main>
    </>
  );
}
