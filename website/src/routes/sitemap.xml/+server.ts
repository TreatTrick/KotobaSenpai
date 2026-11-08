import { SITE_URL } from '#lib/content.ts';

export const prerender = true;

export const GET = () => new Response(`<?xml version="1.0" encoding="UTF-8"?>
<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
  <url><loc>${SITE_URL}/</loc></url>
  <url><loc>${SITE_URL}/zh/</loc></url>
</urlset>`, { headers: { 'content-type': 'application/xml; charset=utf-8' } });
