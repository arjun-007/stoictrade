import type { Metadata } from "next";
import { Inter } from "next/font/google";
import "./globals.css";
import AuthGuard from "@/components/AuthGuard";

const inter = Inter({ subsets: ["latin"] });

export const metadata: Metadata = {
  title: "StoicTrade",
  description: "High-performance algorithmic and manual trading application.",
};

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <html lang="en">
      <body className={`${inter.className} antialiased bg-background text-foreground flex min-h-screen`}>
        <AuthGuard>
          {children}
        </AuthGuard>
      </body>
    </html>
  );
}
