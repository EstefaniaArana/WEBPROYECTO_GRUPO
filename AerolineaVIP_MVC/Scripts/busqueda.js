// Script del formulario "Encuentra tu vuelo" (pantallas 1 y 5).
// Carga los destinos según el origen y valida los datos antes de enviar.
$(function () {
    var $form = $('#formBusqueda');
    if ($form.length === 0) { return; }

    var urlDestinos = $form.attr('data-destinos-url');
    var hoy = $form.attr('data-hoy');
    var destinoInicial = $form.attr('data-destino-inicial');

    function mostrarErrores(lista) {
        $('#errores').html(lista.join('<br />')).show();
    }

    // Carga en la lista "Destino" solo los destinos posibles desde el origen elegido
    function cargarDestinos(seleccionar) {
        var origen = $('#origen').val();
        var $destino = $('#destino');
        $destino.empty().append('<option value="">Seleccionar destino</option>');
        $('#btnBuscar').prop('disabled', false);
        if (!origen) { return; }

        $.getJSON(urlDestinos, { origen: origen }, function (lista) {
            if (lista.length === 0) {
                $destino.empty().append('<option value="">Sin destinos disponibles</option>');
                $('#btnBuscar').prop('disabled', true);
                return;
            }
            $.each(lista, function (i, x) {
                $destino.append($('<option>').val(x.codigo).text(x.texto));
            });
            if (seleccionar) { $destino.val(seleccionar); }
        }).fail(function () {
            mostrarErrores(['No se pudieron cargar los destinos.']);
        });
    }

    $('#origen').on('change', function () { cargarDestinos(''); });
    cargarDestinos(destinoInicial);

    // Botón "Limpiar filtros"
    $('#btnLimpiar').on('click', function () {
        $('#origen').val('UIO');
        $('#fecha').val('');
        $('#pasajeros').val(1);
        $('#errores').hide();
        cargarDestinos('');
    });

    // Validación del cliente al enviar (el servidor la repite en Vuelos/Resultados)
    $form.on('submit', function (e) {
        var errores = [];
        var pasajeros = parseInt($('#pasajeros').val(), 10);

        if (!$('#origen').val()) { errores.push('Seleccione el origen.'); }
        if (!$('#destino').val()) { errores.push('Seleccione el destino.'); }
        if ($('#origen').val() && $('#origen').val() === $('#destino').val()) {
            errores.push('El origen y el destino deben ser distintos.');
        }
        if (!$('#fecha').val()) { errores.push('Indique la fecha de salida.'); }
        else if ($('#fecha').val() < hoy) { errores.push('La fecha no puede ser anterior a hoy.'); }
        if (isNaN(pasajeros) || pasajeros < 1 || pasajeros > 7) {
            errores.push('La cantidad de pasajeros debe estar entre 1 y 7.');
        }

        if (errores.length > 0) {
            e.preventDefault();
            mostrarErrores(errores);
            return;
        }

        $('#errores').hide();
        $('#btnBuscar').prop('disabled', true).text('Buscando…');   // evita envíos repetidos
    });

    // Si el usuario vuelve con el botón "atrás", se reactiva el botón
    $(window).on('pageshow', function () {
        $('#btnBuscar').prop('disabled', false).text('Buscar vuelos');
    });
});