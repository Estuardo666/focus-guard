import { handleAuth } from '../../../../lib/auth.mjs';
export const runtime = 'nodejs';
export const dynamic = 'force-dynamic';
export async function GET(request, context) { return handleAuth(request, (await context.params).action); }
export async function POST(request, context) { return handleAuth(request, (await context.params).action); }
