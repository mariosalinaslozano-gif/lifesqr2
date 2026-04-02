// Cart functionality
let cart = JSON.parse(localStorage.getItem('cart')) || [];

function escapeHtml(value) {
    return String(value)
        .replace(/&/g, "&amp;")
        .replace(/</g, "&lt;")
        .replace(/>/g, "&gt;")
        .replace(/"/g, "&quot;")
        .replace(/'/g, "&#39;");
}

function syncAuthNav() {
    const loggedInUser = localStorage.getItem('loggedInUser');
    const navs = document.querySelectorAll('nav');

    if (!loggedInUser) {
        return;
    }

    navs.forEach(nav => {
        const logoLinks = nav.querySelectorAll('a.navbar-brand[href="index.html"]');
        logoLinks.forEach(link => {
            link.href = 'logged-in.html';
        });

        const loginLinks = nav.querySelectorAll('a[href="login.html"]');
        loginLinks.forEach(link => {
            link.href = 'logged-in.html';
            link.innerHTML = `<i class="bi bi-person-circle me-1"></i>${escapeHtml(loggedInUser)}`;
        });

        const createAccountLinks = nav.querySelectorAll('a[href="create-account.html"]');
        createAccountLinks.forEach(link => {
            link.href = '#';
            link.innerHTML = '<i class="bi bi-box-arrow-right me-1"></i>Logout';
            link.addEventListener('click', function(e) {
                e.preventDefault();
                localStorage.removeItem('loggedInUser');
                window.location.href = 'index.html';
            });
        });
    });
}

function addToCart(productName, price) {
    cart.push({ name: productName, price: price });
    localStorage.setItem('cart', JSON.stringify(cart));
    alert(productName + ' added to cart!');
    updateCartCount();
}

function updateCartCount() {
    const cartLinks = document.querySelectorAll('a[href="cart.html"]');
    cartLinks.forEach(link => {
        if (cart.length > 0) {
            link.innerHTML = `<i class="bi bi-cart3 me-1"></i>Cart <span class="badge bg-danger">${cart.length}</span>`;
            return;
        }

        link.innerHTML = `<i class="bi bi-cart3 me-1"></i>Cart`;
    });
}

function loadCart() {
    if (window.location.pathname.includes('cart.html')) {
        const cartItems = document.getElementById('cartItems');
        const subtotalEl = document.getElementById('subtotal');
        const shippingEl = document.getElementById('shipping');
        const taxEl = document.getElementById('tax');
        const totalEl = document.getElementById('total');
        const checkoutBtn = document.getElementById('checkoutBtn');

        if (cart.length === 0) {
            return;
        }

        let subtotal = 0;
        let html = '';

        cart.forEach((item, index) => {
            subtotal += item.price;
            html += `
                <div class="card mb-3">
                    <div class="card-body">
                        <div class="row align-items-center">
                            <div class="col-md-6">
                                <h5>${item.name}</h5>
                            </div>
                            <div class="col-md-3">
                                <p class="mb-0">$${item.price.toFixed(2)}</p>
                            </div>
                            <div class="col-md-3 text-end">
                                <button class="btn btn-sm btn-danger" onclick="removeFromCart(${index})">Remove</button>
                            </div>
                        </div>
                    </div>
                </div>
            `;
        });

        const shipping = subtotal > 0 ? 9.99 : 0;
        const tax = subtotal * 0.08;
        const total = subtotal + shipping + tax;

        cartItems.innerHTML = html;
        subtotalEl.textContent = '$' + subtotal.toFixed(2);
        shippingEl.textContent = '$' + shipping.toFixed(2);
        taxEl.textContent = '$' + tax.toFixed(2);
        totalEl.textContent = '$' + total.toFixed(2);
        checkoutBtn.disabled = false;
    }
}

function removeFromCart(index) {
    cart.splice(index, 1);
    localStorage.setItem('cart', JSON.stringify(cart));
    location.reload();
}

// Initialize
document.addEventListener('DOMContentLoaded', function() {
    syncAuthNav();
    updateCartCount();
    loadCart();
});

