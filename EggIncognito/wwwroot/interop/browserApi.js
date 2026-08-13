const INTERNAL_HEADER = 'X-EGI-Internal';

function base64FromBuffer(buffer) {
    const bytes = new Uint8Array(buffer);
    const chunk = 0x8000;
    let binary = '';
    for (let i = 0; i < bytes.length; i += chunk) {
        binary += String.fromCharCode.apply(null, bytes.subarray(i, i + chunk));
    }
    return btoa(binary);
}

function bytesFromBase64(base64) {
    const binary = atob(base64);
    const bytes = new Uint8Array(binary.length);
    for (let i = 0; i < binary.length; i++) {
        bytes[i] = binary.charCodeAt(i);
    }
    return bytes;
}

function attachmentName(disposition) {
    if (!disposition) return null;
    const match = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(disposition);
    if (!match) return null;
    try {
        return decodeURIComponent(match[1]);
    } catch {
        return match[1];
    }
}

function textual(contentType) {
    if (!contentType) return true;
    return contentType.includes('json') || contentType.includes('xml') || contentType.startsWith('text/');
}

async function describe(response) {
    const contentType = response.headers.get('content-type') || '';
    const fileName = attachmentName(response.headers.get('content-disposition'));
    if (textual(contentType)) {
        const body = await response.text();
        return {
            status: response.status,
            ok: response.ok,
            contentType,
            body,
            binary: false,
            byteSize: body.length,
            fileName
        };
    }

    const buffer = await response.arrayBuffer();
    return {
        status: response.status,
        ok: response.ok,
        contentType,
        body: base64FromBuffer(buffer),
        binary: true,
        byteSize: buffer.byteLength,
        fileName
    };
}

function failed(error) {
    return {
        status: 0,
        ok: false,
        contentType: '',
        body: String(error && error.message ? error.message : error),
        binary: false,
        byteSize: 0,
        fileName: null
    };
}

export async function send(method, url, body, contentType) {
    const init = {
        method,
        credentials: 'same-origin',
        headers: { [INTERNAL_HEADER]: '1' }
    };
    if (body !== null && body !== undefined) {
        init.headers['Content-Type'] = contentType || 'application/json';
        init.body = body;
    }

    try {
        return await describe(await fetch(url, init));
    } catch (error) {
        return failed(error);
    }
}

export async function sendFile(method, url, field, fileName, base64) {
    const form = new FormData();
    form.append(field, new Blob([bytesFromBase64(base64)]), fileName);
    try {
        return await describe(await fetch(url, {
            method,
            credentials: 'same-origin',
            headers: { [INTERNAL_HEADER]: '1' },
            body: form
        }));
    } catch (error) {
        return failed(error);
    }
}
